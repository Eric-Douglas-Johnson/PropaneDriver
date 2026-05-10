using PropaneDriver.Shared.Enums;

namespace PropaneDriver.Tests;

// Pins the enum-to-wire-string contract. Azure Document Intelligence
// expects very specific prebuilt model identifiers — a typo here would
// blow up at runtime on every OCR call, so we lock the mapping down.
public class AzureDocumentIntelligenceModelExtensionsTests
{
    [Theory]
    [InlineData(AzureDocumentIntelligenceModel.Read, "prebuilt-read")]
    [InlineData(AzureDocumentIntelligenceModel.Layout, "prebuilt-layout")]
    [InlineData(AzureDocumentIntelligenceModel.Document, "prebuilt-document")]
    [InlineData(AzureDocumentIntelligenceModel.Invoice, "prebuilt-invoice")]
    [InlineData(AzureDocumentIntelligenceModel.Receipt, "prebuilt-receipt")]
    [InlineData(AzureDocumentIntelligenceModel.IdDocument, "prebuilt-idDocument")]
    [InlineData(AzureDocumentIntelligenceModel.BusinessCard, "prebuilt-businessCard")]
    public void ToAzureModelId_MapsEachEnumValue_ToExpectedWireString(AzureDocumentIntelligenceModel model, string expected)
    {
        Assert.Equal(expected, model.ToAzureModelId());
    }

    [Fact]
    public void ToAzureModelId_ForUndefinedValue_ThrowsArgumentOutOfRange()
    {
        var undefined = (AzureDocumentIntelligenceModel)999;
        Assert.Throws<ArgumentOutOfRangeException>(() => undefined.ToAzureModelId());
    }
}
