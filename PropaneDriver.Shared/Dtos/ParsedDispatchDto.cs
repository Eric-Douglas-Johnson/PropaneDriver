namespace PropaneDriver.Shared.Dtos
{
    public class ParsedDispatchDto
    {
        public List<ParsedDeliveryDto> Deliveries { get; set; } = new();
        public int PageCount { get; set; }
        public string? Warning { get; set; }
    }
}
