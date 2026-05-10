using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class ImportEndpoints
    {
        public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/imports");

            // OCR (Optical Character Recognition) a dispatch-app screenshot and return the parsed delivery
            // rows as a DTO. The 10 MB cap matches a generous mobile screenshot size
            // and keeps malicious uploads from chewing up the free OCR tier.
            group.MapPost("dispatch-screenshot", async (
                IFormFile file,
                DocumentIntelligenceService docIntelService,
                ILogger<Program> logger,
                CancellationToken cancelToken) =>
            {
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { Message = "No file uploaded." });
                if (file.Length > 10 * 1024 * 1024)
                    return Results.BadRequest(new { Message = "File exceeds 10 MB limit." });
                if (string.IsNullOrEmpty(file.ContentType) || !file.ContentType.StartsWith("image/"))
                    return Results.BadRequest(new { Message = "Only image files are supported." });

                try
                {
                    await using var imgStream = file.OpenReadStream();
                    var ocrData = await docIntelService.RunDocAnalysis(imgStream, cancelToken);
                    var deliveries = DispatchScreenshotParser.Parse(ocrData);

                    return Results.Ok(new ParsedDispatchDto
                    {
                        Deliveries = deliveries.ToList(),
                        PageCount = ocrData.Pages?.Count ?? 0,
                        Warning = deliveries.Count == 0 ? "No addresses detected." : null,
                        // Diagnostic aid: when the parser finds nothing,
                        // hand back the raw OCR lines so the user (or
                        // we) can see what to tune the regex against.
                        RawLines = deliveries.Count == 0
                            ? DispatchScreenshotParser.FlattenLines(ocrData).ToList()
                            : null
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Dispatch screenshot OCR failed");
                    return Results.Problem(
                        detail: ex.Message,
                        title: "OCR failed",
                        statusCode: 502);
                }
            }).DisableAntiforgery().RequireAuthorization("AdminOnly");

            return app;
        }
    }
}
