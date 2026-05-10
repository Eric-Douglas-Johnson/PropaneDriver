using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using PropaneDriver.Server.Services;
using PropaneDriver.Shared.Enums;

namespace PropaneDriver.Tests;

// Verifies the thin pass-through layer the service adds on top of the
// Azure SDK: enum→wire-id translation, stream-to-BinaryData conversion,
// and propagation of the SDK's result. We subclass DocumentIntelligenceClient
// (the Azure SDK's documented mocking seam) so no real Azure call ever fires.
public class DocumentIntelligenceServiceTests
{
    [Fact]
    public async Task RunDocAnalysis_ConvertsEnumModelIdAndForwardsResult()
    {
        var emptyAnalyzeResult = MakeEmptyAnalyzeResult();
        var recordingClient = new RecordingDocumentIntelligenceClient(emptyAnalyzeResult);
        var service = new DocumentIntelligenceService(recordingClient, NullLogger<DocumentIntelligenceService>.Instance);

        var payload = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        using var stream = new MemoryStream(payload);

        var returned = await service.RunDocAnalysis(stream, AzureDocumentIntelligenceModel.Invoice, CancellationToken.None);

        // The service's contract: return the SDK's Operation.Value verbatim
        // and pass the enum-mapped model id through to the client.
        Assert.Same(emptyAnalyzeResult, returned);
        Assert.Equal("prebuilt-invoice", recordingClient.LastModelId);
        Assert.NotNull(recordingClient.LastBytesSource);
        Assert.Equal(payload, recordingClient.LastBytesSource!.ToArray());
    }

    [Fact]
    public async Task RunDocAnalysis_DifferentEnumValues_MapToDifferentModelIds()
    {
        var recordingClient = new RecordingDocumentIntelligenceClient(MakeEmptyAnalyzeResult());
        var service = new DocumentIntelligenceService(recordingClient, NullLogger<DocumentIntelligenceService>.Instance);

        using var stream = new MemoryStream(new byte[] { 1 });
        await service.RunDocAnalysis(stream, AzureDocumentIntelligenceModel.Read, CancellationToken.None);
        Assert.Equal("prebuilt-read", recordingClient.LastModelId);

        stream.Position = 0;
        await service.RunDocAnalysis(stream, AzureDocumentIntelligenceModel.Receipt, CancellationToken.None);
        Assert.Equal("prebuilt-receipt", recordingClient.LastModelId);
    }

    private static AnalyzeResult MakeEmptyAnalyzeResult() =>
        DocumentIntelligenceModelFactory.AnalyzeResult(
            apiVersion: "test",
            modelId: "test",
            contentFormat: null,
            content: string.Empty,
            pages: Array.Empty<DocumentPage>(),
            paragraphs: Array.Empty<DocumentParagraph>(),
            tables: Array.Empty<DocumentTable>(),
            figures: Array.Empty<DocumentFigure>(),
            sections: Array.Empty<DocumentSection>(),
            keyValuePairs: Array.Empty<DocumentKeyValuePair>(),
            styles: Array.Empty<DocumentStyle>(),
            languages: Array.Empty<DocumentLanguage>(),
            documents: Array.Empty<AnalyzedDocument>(),
            warnings: Array.Empty<DocumentIntelligenceWarning>());

    // ---------- Test doubles ----------

    // Subclasses the SDK client (Azure SDK guidelines guarantee the
    // analysis methods are virtual + a parameterless protected ctor)
    // and captures the args passed by the service so tests can assert
    // on them.
    private sealed class RecordingDocumentIntelligenceClient : DocumentIntelligenceClient
    {
        private readonly AnalyzeResult _result;

        public string? LastModelId { get; private set; }
        public BinaryData? LastBytesSource { get; private set; }

        public RecordingDocumentIntelligenceClient(AnalyzeResult result) : base()
        {
            _result = result;
        }

        public override Task<Operation<AnalyzeResult>> AnalyzeDocumentAsync(
            WaitUntil waitUntil,
            string modelId,
            BinaryData bytesSource,
            CancellationToken cancellationToken = default)
        {
            LastModelId = modelId;
            LastBytesSource = bytesSource;
            return Task.FromResult<Operation<AnalyzeResult>>(new ImmediateOperation(_result));
        }
    }

    // Bare-minimum Operation<T> implementation: returns the canned result
    // synchronously. The service only reads .Value, but the abstract
    // contract obliges us to fill in the rest — we throw on the unused
    // members so any future call would surface as an obvious test failure.
    private sealed class ImmediateOperation : Operation<AnalyzeResult>
    {
        private readonly AnalyzeResult _value;

        public ImmediateOperation(AnalyzeResult value) { _value = value; }

        public override AnalyzeResult Value => _value;
        public override bool HasValue => true;
        public override string Id => "test-operation";
        public override bool HasCompleted => true;

        public override Response GetRawResponse() => throw new NotSupportedException();
        public override Response UpdateStatus(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override ValueTask<Response> UpdateStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override ValueTask<Response<AnalyzeResult>> WaitForCompletionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override ValueTask<Response<AnalyzeResult>> WaitForCompletionAsync(TimeSpan pollingInterval, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
