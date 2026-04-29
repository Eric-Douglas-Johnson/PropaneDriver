using Azure.AI.DocumentIntelligence;
using PropaneDriver.Shared.Dtos;
using System.Text.RegularExpressions;

namespace PropaneDriver.Server.Services
{
    public static class DispatchScreenshotParser
    {
        // Matches lines like "1234 Main St" or "47 N County Road 12 SW".
        // The trailing suffix list covers the common US road-type abbreviations
        // that appear on dispatch tickets; extend if real-world data shows gaps.
        private static readonly Regex StreetRegex = new(
            @"^\s*\d{1,6}\s+[A-Za-z0-9\.\-'#/ ]+?\s+(St|Street|Ave|Avenue|Rd|Road|Dr|Drive|Ln|Lane|Blvd|Boulevard|Hwy|Highway|Ct|Court|Cir|Circle|Way|Pl|Place|Pkwy|Parkway|Ter|Terrace|Trl|Trail|Loop)\b\.?(\s+(N|S|E|W|NE|NW|SE|SW))?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "City, ST 12345" or "City ST 12345-6789". Comma is optional because
        // OCR sometimes drops punctuation.
        private static readonly Regex CityStateZipRegex = new(
            @"^\s*([A-Za-z\.\-'’ ]+?),?\s+([A-Z]{2})\s+(\d{5})(?:-\d{4})?\s*$",
            RegexOptions.Compiled);

        // Exposes the parser's view of the OCR result as plain top-to-bottom
        // text lines. Used by the import endpoint to attach a diagnostic
        // dump when no addresses are detected, so we can see what we're
        // actually trying to match.
        public static IReadOnlyList<string> FlattenLines(AnalyzeResult ocrResult)
            => Flatten(ocrResult).Select(l => l.Text).ToList();

        public static IReadOnlyList<ParsedDeliveryDto> Parse(AnalyzeResult ocrResult)
        {
            var lines = Flatten(ocrResult);
            if (lines.Count == 0) return Array.Empty<ParsedDeliveryDto>();

            var results = new List<ParsedDeliveryDto>();
            var consumedAsCustomer = new HashSet<int>();

            for (var i = 0; i < lines.Count; i++)
            {
                var streetMatch = StreetRegex.Match(lines[i].Text);
                if (!streetMatch.Success) continue;

                // City/State/Zip is normally the very next line. Allow up to
                // 2 lines down to tolerate stray apartment/suite lines.
                string? city = null, state = null, zip = null;
                var consumedExtra = -1;
                for (var j = i + 1; j <= Math.Min(i + 2, lines.Count - 1); j++)
                {
                    var cszMatch = CityStateZipRegex.Match(lines[j].Text);
                    if (cszMatch.Success)
                    {
                        city = cszMatch.Groups[1].Value.Trim();
                        state = cszMatch.Groups[2].Value.Trim().ToUpperInvariant();
                        zip = cszMatch.Groups[3].Value.Trim();
                        consumedExtra = j;
                        break;
                    }
                }
                if (city is null || state is null || zip is null) continue;

                // Customer name = closest preceding line that isn't itself an
                // address row and hasn't already been claimed by a previous
                // delivery. Walk upward at most 4 lines to bound scope.
                string customer = string.Empty;
                for (var k = i - 1; k >= Math.Max(0, i - 4); k--)
                {
                    if (consumedAsCustomer.Contains(k)) continue;
                    var text = lines[k].Text.Trim();
                    if (text.Length == 0) continue;
                    if (StreetRegex.IsMatch(text)) break;          // hit the previous delivery's street
                    if (CityStateZipRegex.IsMatch(text)) continue;  // skip the previous delivery's city line
                    customer = text;
                    consumedAsCustomer.Add(k);
                    break;
                }

                results.Add(new ParsedDeliveryDto
                {
                    CustomerName = customer,
                    Street = streetMatch.Value.Trim(),
                    City = city,
                    State = state,
                    ZipCode = zip
                });

                if (consumedExtra >= 0) i = consumedExtra;  // skip past the city/state/zip line
            }

            return results;
        }

        private record FlatLine(string Text, double Y, double X);

        private static List<FlatLine> Flatten(AnalyzeResult ocrResult)
        {
            var flat = new List<FlatLine>();
            var pages = ocrResult.Pages;
            if (pages is null) return flat;

            for (var p = 0; p < pages.Count; p++)
            {
                var page = pages[p];
                if (page?.Lines is null) continue;

                // Pages stack vertically: bias each page's Y so cross-page
                // ordering stays top-to-bottom even if pages share a coord
                // system that resets per page.
                var pageOffset = p * 100_000.0;

                foreach (var line in page.Lines)
                {
                    var (avgY, avgX) = AverageXY(line.Polygon);
                    flat.Add(new FlatLine(line.Content ?? string.Empty, pageOffset + avgY, avgX));
                }
            }

            // Group lines by row (within ~10 units vertically) then sort by X
            // within each row. This handles screenshots where the customer
            // name and address are on the same visual line but split by the
            // OCR engine into separate Line entries.
            return flat
                .OrderBy(l => l.Y)
                .ThenBy(l => l.X)
                .ToList();
        }

        private static (double Y, double X) AverageXY(IReadOnlyList<float>? polygon)
        {
            // Document Intelligence returns the polygon as a flat list of 8
            // floats: [x1,y1,x2,y2,x3,y3,x4,y4]. Average the four corners.
            if (polygon is null || polygon.Count < 2) return (0, 0);

            double sumX = 0, sumY = 0;
            var pairs = polygon.Count / 2;
            for (var i = 0; i < pairs; i++)
            {
                sumX += polygon[i * 2];
                sumY += polygon[i * 2 + 1];
            }
            return (sumY / pairs, sumX / pairs);
        }
    }
}
