using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using System.Diagnostics;

namespace PropaneDriver.Server.Services
{
    public class DocumentIntelligenceService
    {
        private readonly DocumentIntelligenceClient _client;
        private readonly ILogger<DocumentIntelligenceService> _logger;

        public DocumentIntelligenceService(IConfiguration configuration, ILogger<DocumentIntelligenceService> logger)
        {
            _logger = logger;

            var endpoint = configuration["DocumentIntelligence:Endpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("DocumentIntelligence:Endpoint not configured.");

            var apiKey = configuration["DocumentIntelligence:ApiKey"];
            _client = string.IsNullOrWhiteSpace(apiKey)
                ? new DocumentIntelligenceClient(new Uri(endpoint), new DefaultAzureCredential())
                : new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        }

        public async Task<AnalyzeResult> RunDocAnalysis(Stream stream, CancellationToken cancelToken)
        {
            var stopWatch = Stopwatch.StartNew();
            var binaryData = await BinaryData.FromStreamAsync(stream, cancelToken);

            var operation = await _client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                binaryData,
                cancellationToken: cancelToken);

            var docAnalysisResult = operation.Value;

            _logger.LogInformation(
                "Document Intelligence prebuilt-read finished in {ElapsedMs} ms; {PageCount} page(s)",
                stopWatch.ElapsedMilliseconds,
                docAnalysisResult.Pages?.Count ?? 0);

            return docAnalysisResult;
        }
    }
}
