using Azure.AI.DocumentIntelligence;
using PropaneDriver.Shared.Dtos;
using System.Text.RegularExpressions;

namespace PropaneDriver.Server.Services
{
    public static class DispatchScreenshotParser
    {
        // Combined-line address: "3452 Clyde Rd Eveleth, MN" or
        // "1234 Main St Saint Paul, MN 55101". Street starts with a house
        // number, city is the (single) word right before the comma, state
        // is two uppercase letters, zip is optional. This is the primary
        // shape produced by the dispatch app.
        private static readonly Regex AddressLineRegex = new(
            @"^\s*(?<street>\d{1,6}\s+\S.+?)\s+(?<city>[A-Za-z][A-Za-z\-'.]*),\s+(?<state>[A-Z]{2})(?:\s+(?<zip>\d{5}(?:-\d{4})?))?\s*$",
            RegexOptions.Compiled);

        // Fallback for the alternate layout where the street is on its
        // own line: "1234 Main St" / "Springfield, IL 62704".
        private static readonly Regex StreetOnlyRegex = new(
            @"^\s*\d{1,6}\s+[A-Za-z0-9\.\-'#/ ]+?\s+(St|Street|Ave|Avenue|Rd|Road|Dr|Drive|Ln|Lane|Blvd|Boulevard|Hwy|Highway|Ct|Court|Cir|Circle|Way|Pl|Place|Pkwy|Parkway|Ter|Terrace|Trl|Trail|Loop)\b\.?(\s+(N|S|E|W|NE|NW|SE|SW))?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "City, ST 12345" or "City ST 12345-6789". Used as the second
        // half of the multi-line fallback above.
        private static readonly Regex CityStateZipRegex = new(
            @"^\s*([A-Za-z\.\-'’ ]+?),?\s+([A-Z]{2})\s+(\d{5})(?:-\d{4})?\s*$",
            RegexOptions.Compiled);

        // Lines that are clearly noise around each delivery row in the
        // dispatch app: gallon counts, the product name, single-digit
        // row numbers, phone numbers, and stray UI glyphs (>, <, V,
        // ellipsis). Used to skip over them when walking up to find the
        // customer name.
        private static readonly Regex JunkLineRegex = new(
            @"^\s*(?:\d{1,3}|\d+(?:\.\d+)?\s*gal\.?|propane|\(\d{3}\)\s*\d{3}-\d{4}|>+|<+|[Vv]|\.{2,})\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private record FlatLine(string Text, double Y, double X);

        // Exposes the parser's view of the OCR result as plain top-to-bottom
        // text lines. Used by the import endpoint to attach a diagnostic
        // dump when no addresses are detected, so we can see what we're
        // actually trying to match.
        public static IReadOnlyList<string> FlattenLines(AnalyzeResult ocrResult)
            => Flatten(ocrResult).Select(l => l.Text).ToList();

        public static IReadOnlyList<ParsedDeliveryDto> Parse(AnalyzeResult ocrData)
        {
            var lines = Flatten(ocrData);
            if (lines.Count == 0) return Array.Empty<ParsedDeliveryDto>();

            var results = new List<ParsedDeliveryDto>();
            var consumedAsCustomer = new HashSet<int>();

            for (var i = 0; i < lines.Count; i++)
            {
                // Primary format: street + city + state (+ optional zip) on
                // a single OCR line.
                var combined = AddressLineRegex.Match(lines[i].Text);
                if (combined.Success)
                {
                    results.Add(new ParsedDeliveryDto
                    {
                        CustomerName = FindCustomerNameAbove(lines, i, consumedAsCustomer),
                        Street = combined.Groups["street"].Value.Trim(),
                        City = combined.Groups["city"].Value.Trim(),
                        State = combined.Groups["state"].Value.Trim().ToUpperInvariant(),
                        ZipCode = combined.Groups["zip"].Success
                            ? combined.Groups["zip"].Value.Trim()
                            : string.Empty
                    });
                    continue;
                }

                // Fallback: street alone on this line, city/state/zip on
                // the next (with possible stray suite/unit line between).
                if (!StreetOnlyRegex.IsMatch(lines[i].Text)) continue;

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

                results.Add(new ParsedDeliveryDto
                {
                    CustomerName = FindCustomerNameAbove(lines, i, consumedAsCustomer),
                    Street = lines[i].Text.Trim(),
                    City = city,
                    State = state,
                    ZipCode = zip
                });

                if (consumedExtra >= 0) i = consumedExtra;
            }

            return results;
        }

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

        // Walks upward from an address line to find the customer name,
        // skipping known noise (gallons, "Propane", row#, phone, glyphs)
        // and stopping when it hits the previous delivery's address.
        private static string FindCustomerNameAbove(
            List<FlatLine> lines, int addressIndex, HashSet<int> consumed)
        {
            for (var k = addressIndex - 1; k >= Math.Max(0, addressIndex - 8); k--)
            {
                if (consumed.Contains(k)) continue;
                var text = lines[k].Text.Trim();
                if (text.Length == 0) continue;
                if (JunkLineRegex.IsMatch(text)) continue;
                if (AddressLineRegex.IsMatch(text)) break;
                if (StreetOnlyRegex.IsMatch(text)) break;
                if (CityStateZipRegex.IsMatch(text)) continue;

                consumed.Add(k);
                return text;
            }
            return string.Empty;
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
