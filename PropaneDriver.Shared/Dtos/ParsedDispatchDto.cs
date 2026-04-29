namespace PropaneDriver.Shared.Dtos
{
    public class ParsedDispatchDto
    {
        public List<ParsedDeliveryDto> Deliveries { get; set; } = new();
        public int PageCount { get; set; }
        public string? Warning { get; set; }

        // Populated only when Deliveries is empty, to help diagnose why the
        // parser found no matches. Each entry is one OCR'd line in
        // top-to-bottom reading order.
        public List<string>? RawLines { get; set; }
    }
}
