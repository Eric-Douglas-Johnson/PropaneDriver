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

            // Hoover-specific two-image scan: the equipment image goes through
            // the Read model and feeds the EquipmentPieces list, while the
            // invoice image goes through the Invoice model and populates the
            // standard InvoiceData header/line-item fields. Both run on the
            // same upload so the client can present a single combined result.
            group.MapPost("tools-hoover-invoice", async (
                IFormFile equipmentImage,
                IFormFile invoiceImage,
                DocumentIntelligenceService docIntelService,
                ILogger<Program> logger,
                CancellationToken cancelToken) =>
            {
                const long maxBytes = 10 * 1024 * 1024;
                foreach (var (uploadedFile, fileLabel) in new[]
                {
                    (equipmentImage, "equipment"),
                    (invoiceImage, "invoice")
                })
                {
                    if (uploadedFile is null || uploadedFile.Length == 0)
                        return Results.BadRequest(new { Message = $"No {fileLabel} image uploaded." });
                    if (uploadedFile.Length > maxBytes)
                        return Results.BadRequest(new { Message = $"{fileLabel} image exceeds 10 MB limit." });
                    if (string.IsNullOrEmpty(uploadedFile.ContentType) || !uploadedFile.ContentType.StartsWith("image/"))
                        return Results.BadRequest(new { Message = $"{fileLabel} must be an image file." });
                }

                try
                {
                    await using var equipmentImageStream = equipmentImage.OpenReadStream();
                    var equipmentOcrResult = await docIntelService.RunDocAnalysis(
                        equipmentImageStream,
                        AzureDocumentIntelligenceModel.Read,
                        cancelToken);

                    await using var invoiceImageStream = invoiceImage.OpenReadStream();
                    var invoiceOcrResult = await docIntelService.RunDocAnalysis(
                        invoiceImageStream,
                        AzureDocumentIntelligenceModel.Invoice,
                        cancelToken);

                    // Build the combined HooverInvoiceData: equipment list
                    // from the Read OCR, then have the invoice mapper layer
                    // the standard fields on top of the same instance so
                    // both halves end up on one object.
                    var hooverInvoiceData = new HooverInvoiceData
                    {
                        EquipmentPieces = HooverEquipmentParserService.Parse(equipmentOcrResult)
                    };
                    InvoiceDataMapperService.PopulateFromAnalyzeResult(invoiceOcrResult, hooverInvoiceData);

                    // Surface the raw OCR lines from both images so the user
                    // can sanity-check what Azure read. Equipment pages come
                    // first; invoice pages continue the numbering after them.
                    var combinedOcrPages = new List<OcrPageDto>();
                    AppendOcrPages(equipmentOcrResult, combinedOcrPages);
                    AppendOcrPages(invoiceOcrResult, combinedOcrPages);

                    return Results.Ok(new OcrDocumentDto
                    {
                        FileName = $"{equipmentImage.FileName} + {invoiceImage.FileName}",
                        PageCount = combinedOcrPages.Count,
                        Pages = combinedOcrPages,
                        Invoice = hooverInvoiceData
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Hoover invoice scan failed");
                    return Results.Problem(
                        detail: ex.Message,
                        title: "OCR failed",
                        statusCode: 502);
                }
            }).DisableAntiforgery().RequireAuthorization("AdminOnly");

            return app;
        }

        // Walks an AnalyzeResult's pages and appends them to the destination
        // list using the page's own number when available, falling back to
        // a continuing sequence so combined multi-image scans stay readable.
        private static void AppendOcrPages(AnalyzeResult ocrResult, List<OcrPageDto> destination)
        {
            if (ocrResult.Pages is null) return;
            var fallbackPageIndex = destination.Count;
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

                destination.Add(new OcrPageDto
                {
                    PageNumber = page.PageNumber > 0 ? page.PageNumber : fallbackPageIndex,
                    Lines = pageLines
                });
            }
        }
    }
}
