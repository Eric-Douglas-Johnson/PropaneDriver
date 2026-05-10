using Azure;
using Azure.AI.DocumentIntelligence;
using PropaneDriver.Shared.Enums;
using System.Diagnostics;

namespace PropaneDriver.Server.Services
{
    public class DocumentIntelligenceService
    {
        private readonly DocumentIntelligenceClient _client;
        private readonly ILogger<DocumentIntelligenceService> _logger;

        // Takes the SDK client directly so tests can subclass it and override
        // AnalyzeDocumentAsync with deterministic results. Production wiring
        // (see Program.cs) builds the real client from configuration.
        public DocumentIntelligenceService(DocumentIntelligenceClient client, ILogger<DocumentIntelligenceService> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<AnalyzeResult> RunDocAnalysis(Stream stream, AzureDocumentIntelligenceModel model, CancellationToken cancelToken)
        {
            var modelId = model.ToAzureModelId();

            var stopWatch = Stopwatch.StartNew();
            var binaryData = await BinaryData.FromStreamAsync(stream, cancelToken);

            //Take a look at prebuilt-invoice model for Jean's solution
            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                modelId,
                binaryData,
                cancellationToken: cancelToken);

            var docAnalysisResult = operation.Value;

            _logger.LogInformation(
                "Document Intelligence {ModelId} finished in {ElapsedMs} ms; {PageCount} page(s)",
                modelId,
                stopWatch.ElapsedMilliseconds,
                docAnalysisResult.Pages?.Count ?? 0);

            return docAnalysisResult;
        }
    }
}
