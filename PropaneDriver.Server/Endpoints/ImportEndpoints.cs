using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Endpoints
{
    public static class ImportEndpoints
    {
        public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("api/imports");

            // OCR a dispatch-app screenshot and return the parsed delivery
            // rows as a DTO. The client uses these to pre-fill the existing
            // Add Delivery form on Admin.razor — nothing is written to the DB
            // here. The 10 MB cap matches a generous mobile screenshot size
            // and keeps malicious uploads from chewing up the free OCR tier.
            group.MapPost("dispatch-screenshot", async (
                IFormFile file,
                DocumentIntelligenceService docs,
                ILogger<Program> logger,
                CancellationToken ct) =>
            {
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { Message = "No file uploaded." });
                if (file.Length > 10 * 1024 * 1024)
                    return Results.BadRequest(new { Message = "File exceeds 10 MB limit." });
                if (string.IsNullOrEmpty(file.ContentType) || !file.ContentType.StartsWith("image/"))
                    return Results.BadRequest(new { Message = "Only image files are supported." });

                try
                {
                    await using var stream = file.OpenReadStream();
                    var ocr = await docs.AnalyzeReadAsync(stream, ct);
                    var deliveries = DispatchScreenshotParser.Parse(ocr);

                    return Results.Ok(new ParsedDispatchDto
                    {
                        Deliveries = deliveries.ToList(),
                        PageCount = ocr.Pages?.Count ?? 0,
                        Warning = deliveries.Count == 0 ? "No addresses detected." : null
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
            }).DisableAntiforgery();

            return app;
        }
    }
}
