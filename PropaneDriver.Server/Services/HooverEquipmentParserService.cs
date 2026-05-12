using System.Globalization;
using System.Text.RegularExpressions;
using Azure.AI.DocumentIntelligence;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Services
{
    // Parses the Read-model OCR result for the equipment-list image that
    // accompanies a Hoover invoice. The relevant content lives in a window
    // bounded by two sentinel strings on the form: it begins after "START"
    // and ends before "HOOVER SUPERVISOR". Inside that window the OCR yields
    // an even count of numbers pairing equipment id to fuel quantity, then
    // a final trailing number for the total fuel pumped across the run.
    public static class HooverEquipmentParserService
    {
        private static readonly Regex NumberTokenRegex = new(
            @"\d+(?:\.\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Word-boundary anchored so a longer string containing "START" (e.g.
        // "STARTING ODOMETER") doesn't accidentally open the window early.
        private static readonly Regex StartSentinelRegex = new(
            @"\bSTART\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex EndSentinelRegex = new(
            @"\bHOOVER\s+SUPERVISOR\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static HooverEquipmentScanResult Parse(AnalyzeResult analyzeResult)
        {
            var scanResult = new HooverEquipmentScanResult();
            if (analyzeResult.Pages is null) return scanResult;

            // Flatten every OCR line into one in-order list so the START /
            // HOOVER SUPERVISOR window can span page breaks if it has to.
            var orderedOcrLines = new List<string>();
            foreach (var page in analyzeResult.Pages)
            {
                if (page.Lines is null) continue;
                foreach (var line in page.Lines)
                {
                    if (!string.IsNullOrWhiteSpace(line.Content))
                        orderedOcrLines.Add(line.Content);
                }
            }

            var (startLineIndex, endLineIndex) = LocateSentinelWindow(orderedOcrLines);
            if (startLineIndex < 0 || endLineIndex < 0) return scanResult;

            // Pull every numeric token that falls strictly between the
            // sentinel lines, in reading order.
            var numericTokens = new List<string>();
            for (var lineIndex = startLineIndex + 1; lineIndex < endLineIndex; lineIndex++)
            {
                foreach (Match numberMatch in NumberTokenRegex.Matches(orderedOcrLines[lineIndex]))
                    numericTokens.Add(numberMatch.Value);
            }

            if (numericTokens.Count == 0) return scanResult;

            // Last token is the total fuel pumped. Pull it off first so the
            // remaining tokens line up as clean (id, fuel) pairs.
            if (decimal.TryParse(
                    numericTokens[numericTokens.Count - 1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var totalFuelPumped))
            {
                scanResult.TotalFuelPumped = totalFuelPumped;
            }

            var pairableTokenCount = numericTokens.Count - 1;
            for (var pairIndex = 0; pairIndex + 1 < pairableTokenCount; pairIndex += 2)
            {
                if (!int.TryParse(
                        numericTokens[pairIndex],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var equipmentId))
                    continue;

                if (!decimal.TryParse(
                        numericTokens[pairIndex + 1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var fuelQuantity))
                    continue;

                scanResult.EquipmentPieces.Add(new EquipmentPiece
                {
                    Id = equipmentId,
                    FuelQuantity = fuelQuantity
                });
            }

            return scanResult;
        }

        // Locates the first "START" line and the first "HOOVER SUPERVISOR"
        // line that follows it. Returns (-1, -1) if either sentinel is
        // missing — the caller treats that as "nothing to extract" rather
        // than guessing.
        private static (int StartLineIndex, int EndLineIndex) LocateSentinelWindow(List<string> orderedOcrLines)
        {
            var startLineIndex = -1;
            for (var lineIndex = 0; lineIndex < orderedOcrLines.Count; lineIndex++)
            {
                if (startLineIndex < 0)
                {
                    if (StartSentinelRegex.IsMatch(orderedOcrLines[lineIndex]))
                        startLineIndex = lineIndex;
                }
                else if (EndSentinelRegex.IsMatch(orderedOcrLines[lineIndex]))
                {
                    return (startLineIndex, lineIndex);
                }
            }
            return (-1, -1);
        }
    }

    // Bundles the two outputs of the equipment-image parse — the per-piece
    // list and the trailing total — so the caller can assign both to the
    // HooverInvoiceData in one shot.
    public class HooverEquipmentScanResult
    {
        public List<EquipmentPiece> EquipmentPieces { get; set; } = new();
        public decimal? TotalFuelPumped { get; set; }
    }
}
