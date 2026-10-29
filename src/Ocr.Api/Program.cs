using System.Text.Json.Serialization;
using Ocr.Api;

var builder = WebApplication.CreateSlimBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));

var app = builder.Build();

// Keep unexpected inference errors out of responses without hiding them from server logs.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsync("OCR processing failed.");
}));

app.MapGet("/health", () => TypedResults.Ok("ok"));

app.MapPost("/api/ocr", async Task<IResult> (HttpRequest request, CancellationToken cancellationToken) =>
{
    if (request.ContentLength is 0 or > 10 * 1024 * 1024)
    {
        return TypedResults.BadRequest("Image must be between 1 byte and 10 MB.");
    }

    if (request.ContentType is not ("image/jpeg" or "image/png"))
    {
        return TypedResults.BadRequest("Content-Type must be image/jpeg or image/png.");
    }

    OcrResponse? result = await OcrEngine.RecognizeAsync(request.Body, cancellationToken);
    return result is null
        ? TypedResults.BadRequest("Invalid image or image dimensions exceed 20 megapixels.")
        : TypedResults.Ok(result);
});

app.Run();

namespace Ocr.Api
{
    /// <summary>OCR response containing recognized plain text for a single uploaded image.</summary>
    public sealed class OcrResponse
    {
        /// <summary>Recognized text in reading order; empty when no text is detected.</summary>
        public required string Text { get; init; }
    }

    [JsonSerializable(typeof(OcrResponse))]
    [JsonSerializable(typeof(string))]
    /// <summary>Source-generated JSON metadata for the slim API's AOT-safe HTTP responses.</summary>
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }
}
