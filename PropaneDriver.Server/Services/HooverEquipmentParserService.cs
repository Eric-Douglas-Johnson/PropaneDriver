using System.Globalization;
using System.Text.RegularExpressions;
using Azure.AI.DocumentIntelligence;
using PropaneDriver.Shared.Dtos;

namespace PropaneDriver.Server.Services
{
    // Parses the Read-model OCR result for the equipment-list image that
    // accompanies a Hoover invoice. Each row is expected to be two numbers
    // separated by whitespace — first is the equipment id, second is the
    // fuel quantity for that piece of equipment, e.g. "1042   45.5". Lines
    // that don't contain at least two numeric tokens are skipped (header
    // rows, blank lines, footers, etc.).
    public static class HooverEquipmentParserService
    {
        private static readonly Regex NumberTokenRegex = new(
            @"\d+(?:\.\d+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static List<EquipmentPiece> Parse(AnalyzeResult analyzeResult)
        {
            var equipmentPieces = new List<EquipmentPiece>();
            if (analyzeResult.Pages is null) return equipmentPieces;

            foreach (var page in analyzeResult.Pages)
            {
                if (page.Lines is null) continue;
                foreach (var line in page.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.Content)) continue;

                    var numberMatches = NumberTokenRegex.Matches(line.Content);
                    if (numberMatches.Count < 2) continue;

                    // First number is the equipment id (integer); the last
                    // number on the line is the fuel quantity (decimal). Any
                    // middle numbers — running totals, page numbers, etc. —
                    // are ignored so the parser stays robust against
                    // incidental OCR noise.
                    if (!int.TryParse(
                            numberMatches[0].Value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var equipmentId))
                        continue;

                    var lastNumberToken = numberMatches[numberMatches.Count - 1].Value;
                    if (!decimal.TryParse(
                            lastNumberToken,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var fuelQuantity))
                        continue;

                    equipmentPieces.Add(new EquipmentPiece
                    {
                        Id = equipmentId,
                        FuelQuantity = fuelQuantity
                    });
                }
            }

            return equipmentPieces;
        }
    }
}
