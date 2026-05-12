using Azure.AI.DocumentIntelligence;
using PropaneDriver.Server.Services;

namespace PropaneDriver.Tests;

// Pure unit tests for the dispatch-screenshot parser. We build synthetic
// AnalyzeResult inputs via DocumentIntelligenceModelFactory so we never
// hit the real Azure service — these tests prove the regex/coordinate
// logic, not OCR quality.
public class DispatchScreenshotParserServiceTests
{
    // Lines are positioned vertically by ascending Y so the parser's
    // polygon-average sort reads them top-to-bottom in the order passed.
    private static AnalyzeResult BuildPageResult(params string[] lines)
    {
        var documentLines = new List<DocumentLine>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var topY = (float)(lineIndex * 10);
            var bottomY = topY + 5f;
            var polygon = new float[] { 0f, topY, 10f, topY, 10f, bottomY, 0f, bottomY };
            documentLines.Add(DocumentIntelligenceModelFactory.DocumentLine(
                content: lines[lineIndex],
                polygon: polygon,
                spans: Array.Empty<DocumentSpan>()));
        }

        var page = DocumentIntelligenceModelFactory.DocumentPage(
            pageNumber: 1,
            angle: 0f,
            width: 100f,
            height: 1000f,
            unit: LengthUnit.Pixel,
            spans: Array.Empty<DocumentSpan>(),
            words: Array.Empty<DocumentWord>(),
            selectionMarks: Array.Empty<DocumentSelectionMark>(),
            lines: documentLines,
            barcodes: Array.Empty<DocumentBarcode>(),
            formulas: Array.Empty<DocumentFormula>());

        return DocumentIntelligenceModelFactory.AnalyzeResult(
            apiVersion: "test",
            modelId: "test",
            contentFormat: null,
            content: string.Join("\n", lines),
            pages: new[] { page },
            paragraphs: Array.Empty<DocumentParagraph>(),
            tables: Array.Empty<DocumentTable>(),
            figures: Array.Empty<DocumentFigure>(),
            sections: Array.Empty<DocumentSection>(),
            keyValuePairs: Array.Empty<DocumentKeyValuePair>(),
            styles: Array.Empty<DocumentStyle>(),
            languages: Array.Empty<DocumentLanguage>(),
            documents: Array.Empty<AnalyzedDocument>(),
            warnings: Array.Empty<DocumentIntelligenceWarning>());
    }

    [Fact]
    public void FlattenLines_EmptyResult_ReturnsEmpty()
    {
        var result = DispatchScreenshotParserService.FlattenLines(BuildPageResult());
        Assert.Empty(result);
    }

    [Fact]
    public void FlattenLines_PreservesTopToBottomOrder()
    {
        var result = DispatchScreenshotParserService.FlattenLines(BuildPageResult("first", "second", "third"));
        Assert.Equal(new[] { "first", "second", "third" }, result);
    }

    [Fact]
    public void Parse_NoMatchingLines_ReturnsEmpty()
    {
        var result = DispatchScreenshotParserService.Parse(BuildPageResult("hello", "world"));
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_CombinedAddressLine_ProducesOneDeliveryWithCustomerName()
    {
        var result = DispatchScreenshotParserService.Parse(BuildPageResult(
            "Jane Doe",
            "1234 Main St Springfield, IL 62704"));

        var delivery = Assert.Single(result);
        Assert.Equal("Jane Doe", delivery.CustomerName);
        Assert.Equal("1234 Main St", delivery.Street);
        Assert.Equal("Springfield", delivery.City);
        Assert.Equal("IL", delivery.State);
        Assert.Equal("62704", delivery.ZipCode);
    }

    [Fact]
    public void Parse_StreetAndCityStateZipOnSeparateLines_ProducesOneDelivery()
    {
        // City "Saint Paul" has a space, which the combined-line regex
        // doesn't allow — forces the parser into its multi-line fallback.
        var result = DispatchScreenshotParserService.Parse(BuildPageResult(
            "Bob Smith",
            "456 Elm Avenue",
            "Saint Paul, MN 55101"));

        var delivery = Assert.Single(result);
        Assert.Equal("Bob Smith", delivery.CustomerName);
        Assert.Equal("456 Elm Avenue", delivery.Street);
        Assert.Equal("Saint Paul", delivery.City);
        Assert.Equal("MN", delivery.State);
        Assert.Equal("55101", delivery.ZipCode);
    }

    [Fact]
    public void Parse_SkipsJunkLinesWhenLookingForCustomerName()
    {
        // "150 gal." and "Propane" both match the JunkLineRegex — the
        // customer-name walk-upward should skip past both.
        var result = DispatchScreenshotParserService.Parse(BuildPageResult(
            "Mary Johnson",
            "150 gal.",
            "Propane",
            "999 Oak St Madison, WI 53703"));

        var delivery = Assert.Single(result);
        Assert.Equal("Mary Johnson", delivery.CustomerName);
        Assert.Equal("999 Oak St", delivery.Street);
        Assert.Equal("Madison", delivery.City);
    }

    [Fact]
    public void Parse_MultipleDeliveries_ReturnsEachWithItsOwnCustomerName()
    {
        var result = DispatchScreenshotParserService.Parse(BuildPageResult(
            "Alice",
            "111 First St Eveleth, MN 55734",
            "Bob",
            "222 Second Ave Hibbing, MN 55746"));

        Assert.Equal(2, result.Count);
        Assert.Equal("Alice", result[0].CustomerName);
        Assert.Equal("111 First St", result[0].Street);
        Assert.Equal("Eveleth", result[0].City);
        Assert.Equal("Bob", result[1].CustomerName);
        Assert.Equal("222 Second Ave", result[1].Street);
        Assert.Equal("Hibbing", result[1].City);
    }

    [Fact]
    public void Parse_CombinedAddressWithoutZip_ReturnsEmptyZip()
    {
        var result = DispatchScreenshotParserService.Parse(BuildPageResult(
            "Carol Lee",
            "777 Pine Rd Eveleth, MN"));

        var delivery = Assert.Single(result);
        Assert.Equal("Carol Lee", delivery.CustomerName);
        Assert.Equal("MN", delivery.State);
        Assert.Equal(string.Empty, delivery.ZipCode);
    }
}
