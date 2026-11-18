using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Medium;
using Sdcb.SimdCodecs.Jpeg;
using Sdcb.SimdCodecs.Png;
using System.Diagnostics.CodeAnalysis;

namespace Ocr.Api;

/// <summary>Loads the bundled Chinese OCR model once and serializes inference on the shared engine.</summary>
/// <remarks>Used by the image endpoint; depends on SimdPaddleOCR and the JPEG and PNG SimdCodecs packages.</remarks>
internal static class OcrEngine
{
    private const int MAX_IMAGE_BYTES = 10 * 1024 * 1024;
    private const long MAX_PIXELS = 20_000_000;
    private static readonly Lazy<Task<PaddleOcrAll>> Engine = new(() => PaddleOcrAll.LoadAsync(ChineseV6MediumModels.Default));
    private static readonly SemaphoreSlim InferenceLock = new(1, 1);

    /// <summary>Decodes an uploaded JPEG or PNG and recognizes its text.</summary>
    /// <param name="stream">Readable image body stream, at most 10 MB.</param>
    /// <param name="contentType">Optional MIME type used to choose which supported decoder to try first.</param>
    /// <param name="logger">Logger for reporting a mismatch between the declared and detected image formats.</param>
    /// <param name="cancellationToken">Cancellation from the request.</param>
    /// <returns>Recognized text or null for malformed/oversized images.</returns>
    /// <exception cref="IOException">The body cannot be read.</exception>
    internal static async Task<OcrResponse?> RecognizeAsync(Stream stream, string? contentType, ILogger logger, CancellationToken cancellationToken)
    {
        // Kestrel limits the request body; inspect metadata before allocating decoded pixels.
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length is 0 or > MAX_IMAGE_BYTES)
        {
            return null;
        }

        ReadOnlySpan<byte> content = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        string firstType = contentType == "image/png" ? "image/png" : "image/jpeg";
        string fallbackType = firstType == "image/jpeg" ? "image/png" : "image/jpeg";
        if (!TryDecode(content, firstType, out byte[]? rgba, out int width, out int height))
        {
            logger.LogWarning("Unable to decode image as {ImageType}; trying {FallbackType}. Declared content type: {ContentType}.",
                firstType, fallbackType, contentType);
            if (!TryDecode(content, fallbackType, out rgba, out width, out height))
            {
                return null;
            }

            logger.LogInformation("Image content decoded as {DetectedType}. Declared content type: {ContentType}.",
                fallbackType, contentType);
        }

        // The model is shared across requests; prevent concurrent inference on its mutable sessions.
        await InferenceLock.WaitAsync(cancellationToken);
        try
        {
            PaddleOcrAll engine = await Engine.Value;
            return new OcrResponse { Text = engine.Run(rgba, width, height,
                format: ImagePixelFormat.Rgba32).Text };
        }
        finally
        {
            InferenceLock.Release();
        }
    }

    private static bool TryDecode(ReadOnlySpan<byte> content, string contentType, [NotNullWhen(true)] out byte[]? rgba, out int width, out int height)
    {
        rgba = null;
        width = 0;
        height = 0;
        try
        {
            // Validate dimensions before allocating pixels and require complete file markers.
            if (contentType == "image/jpeg")
            {
                if (!JpegDecoder.TryReadInfo(content, out JpegInfo? info) ||
                    info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MAX_PIXELS ||
                    content.Length < 5 || content[0] != 0xff || content[1] != 0xd8 || content[2] != 0xff ||
                    content[^2] != 0xff || content[^1] != 0xd9)
                {
                    return false;
                }

                width = info.Width;
                height = info.Height;
                byte[] pixels = new byte[width * height * 4];
                JpegDecoder.DecodeTo(content, pixels);
                rgba = pixels;
                return true;
            }

            if (!PngImageDecoder.TryReadInfo(content, out var pngInfo) ||
                pngInfo.Width <= 0 || pngInfo.Height <= 0 || (long)pngInfo.Width * pngInfo.Height > MAX_PIXELS ||
                content.Length < 45 ||
                !content[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                !content.Slice(8, 8).SequenceEqual(new byte[] { 0, 0, 0, 13, 73, 72, 68, 82 }) ||
                !content[^12..].SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }))
            {
                return false;
            }

            width = pngInfo.Width;
            height = pngInfo.Height;
            byte[] pngPixels = new byte[width * height * 4];
            if (!PngImageDecoder.TryDecodeIntoRgba8(content, pngPixels))
            {
                return false;
            }

            rgba = pngPixels;
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
