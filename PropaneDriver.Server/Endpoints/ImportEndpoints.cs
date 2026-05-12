using Azure.AI.DocumentIntelligence;
using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Dtos;
using PropaneDriver.Shared.Enums;

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
                    var ocrData = await docIntelService.RunDocAnalysis(imgStream, AzureDocumentIntelligenceModel.Read, cancelToken);
                    var deliveries = DispatchScreenshotParserService.Parse(ocrData);

                    return Results.Ok(new ParsedDispatchDto
                    {
                        Deliveries = deliveries.ToList(),
                        PageCount = ocrData.Pages?.Count ?? 0,
                        Warning = deliveries.Count == 0 ? "No addresses detected." : null,
                        // Diagnostic aid: when the parser finds nothing,
                        // hand back the raw OCR lines so the user (or
                        // we) can see what to tune the regex against.
                        RawLines = deliveries.Count == 0
                            ? DispatchScreenshotParserService.FlattenLines(ocrData).ToList()
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
            }).DisableAntiforgery().RequireAuthorization("AuthenticatedDriver");

            // Admin Tools document scan: run the same Document Intelligence OCR
            // pipeline as the dispatch screenshot import, but return the raw
            // per-page lines without funneling them through any specific
            // parser. The Tools page builds on top of this as a foundation
            // for future, purpose-specific document workflows.
            group.MapPost("tools-document", async (
                IFormFile file,
                DocumentIntelligenceService docIntelService,
                ILogger<Program> logger,
                CancellationToken cancelToken,
                AzureDocumentIntelligenceModel? model) =>
            {
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { Message = "No file uploaded." });
                if (file.Length > 10 * 1024 * 1024)
                    return Results.BadRequest(new { Message = "File exceeds 10 MB limit." });
                if (string.IsNullOrEmpty(file.ContentType) || !file.ContentType.StartsWith("image/"))
                    return Results.BadRequest(new { Message = "Only image files are supported." });

                // Defaults to Read so callers that don't care about the model
                // still get plain OCR — the Tools page will always send the
                // value, but the contract stays forgiving.
                var modelToUse = model ?? AzureDocumentIntelligenceModel.Read;
                if (!Enum.IsDefined(modelToUse))
                    return Results.BadRequest(new { Message = $"Unknown model: {modelToUse}." });

                try
                {
                    await using var imageStream = file.OpenReadStream();
                    var ocrResult = await docIntelService.RunDocAnalysis(imageStream, modelToUse, cancelToken);

                    var pages = new List<OcrPageDto>();
                    if (ocrResult.Pages is not null)
                    {
                        var fallbackPageIndex = 0;
                        foreach (DocumentPage page in ocrResult.Pages)
                        {
                            fallbackPageIndex++;
                            var pageLines = new List<string>();
                            if (page.Lines is not null)
                            {
                                foreach (DocumentLine line in page.Lines)
                                {
                                    if (!string.IsNullOrEmpty(line.Content))
                                        pageLines.Add(line.Content);
                                }
                            }

                            pages.Add(new OcrPageDto
                            {
                                PageNumber = page.PageNumber > 0 ? page.PageNumber : fallbackPageIndex,
                                Lines = pageLines
                            });
                        }
                    }

                    // When the caller picked the Invoice model, also lift the
                    // structured key/value fields out of Azure's AnalyzedDocument
                    // so the client can render them next to the raw OCR. Other
                    // models don't produce these fields, so we skip the call.
                    InvoiceData? invoiceData = null;
                    if (modelToUse == AzureDocumentIntelligenceModel.Invoice)
                        invoiceData = InvoiceDataMapperService.FromAnalyzeResult(ocrResult);

                    return Results.Ok(new OcrDocumentDto
                    {
                        FileName = file.FileName ?? string.Empty,
                        PageCount = pages.Count,
                        Pages = pages,
                        Invoice = invoiceData
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Tools document OCR failed");
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
