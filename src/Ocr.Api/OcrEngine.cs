using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Medium;
using Sdcb.SimdCodecs.Jpeg;

namespace Ocr.Api;

/// <summary>Loads the bundled Chinese OCR model once and serializes inference on the shared engine.</summary>
/// <remarks>Used by the image endpoint; depends on SimdPaddleOCR and Sdcb.SimdCodecs.Jpeg.</remarks>
internal static class OcrEngine
{
    private const int MAX_IMAGE_BYTES = 10 * 1024 * 1024;
    private const long MAX_PIXELS = 20_000_000;
    private static readonly Lazy<Task<PaddleOcrAll>> Engine = new(() => PaddleOcrAll.LoadAsync(ChineseV6MediumModels.Default));
    private static readonly SemaphoreSlim InferenceLock = new(1, 1);

    /// <summary>Decodes an uploaded JPEG and recognizes its text.</summary>
    /// <param name="stream">Readable image body stream, at most 10 MB.</param>
    /// <param name="cancellationToken">Cancellation from the request.</param>
    /// <returns>Recognized text or null for malformed/oversized images.</returns>
    /// <exception cref="IOException">The body cannot be read.</exception>
    internal static async Task<OcrResponse?> RecognizeAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Kestrel limits the request body; inspect metadata before allocating decoded pixels.
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length is 0 or > MAX_IMAGE_BYTES)
        {
            return null;
        }

        try
        {
            ReadOnlySpan<byte> content = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
            // The decoder can salvage truncated scans; require the JPEG end marker before decoding.
            if (content.Length < 5 || content[0] != 0xff || content[1] != 0xd8 || content[2] != 0xff ||
                content[^2] != 0xff || content[^1] != 0xd9)
            {
                return null;
            }

            if (!JpegDecoder.TryReadInfo(content, out JpegInfo? info) ||
                info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MAX_PIXELS)
            {
                return null;
            }

            byte[] rgba = new byte[info.Width * info.Height * 4];
            JpegDecoder.DecodeTo(content, rgba);

            // The model is shared across requests; prevent concurrent inference on its mutable sessions.
            await InferenceLock.WaitAsync(cancellationToken);
            try
            {
                PaddleOcrAll engine = await Engine.Value;
                return new OcrResponse { Text = engine.Run(rgba, info.Width, info.Height,
                    format: ImagePixelFormat.Rgba32).Text };
            }
            finally
            {
                InferenceLock.Release();
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
