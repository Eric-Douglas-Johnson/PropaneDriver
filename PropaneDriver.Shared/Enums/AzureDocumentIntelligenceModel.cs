namespace PropaneDriver.Shared.Enums
{
    public enum AzureDocumentIntelligenceModel
    {
        Read,
        Layout,
        Document,
        Invoice,
        Receipt,
        IdDocument,
        BusinessCard
    }

    public static class AzureDocumentIntelligenceModelExtensions
    {
        // Maps to the wire-format model ids the Azure SDK expects when
        // calling AnalyzeDocumentAsync. Keeping the mapping next to the
        // enum (rather than as attributes + reflection) avoids per-call
        // metadata lookups and lets the compiler catch new enum values
        // that forget to update the switch.
        public static string ToAzureModelId(this AzureDocumentIntelligenceModel model) => model switch
        {
            AzureDocumentIntelligenceModel.Read => "prebuilt-read",
            AzureDocumentIntelligenceModel.Layout => "prebuilt-layout",
            AzureDocumentIntelligenceModel.Document => "prebuilt-document",
            AzureDocumentIntelligenceModel.Invoice => "prebuilt-invoice",
            AzureDocumentIntelligenceModel.Receipt => "prebuilt-receipt",
            AzureDocumentIntelligenceModel.IdDocument => "prebuilt-idDocument",
            AzureDocumentIntelligenceModel.BusinessCard => "prebuilt-businessCard",
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown Azure Document Intelligence model.")
        };
    }
}
