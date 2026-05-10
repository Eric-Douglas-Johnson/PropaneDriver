namespace PropaneDriver.Shared.Dtos
{
    public class OcrPageDto
    {
        public int PageNumber { get; set; }
        public List<string> Lines { get; set; } = new();
    }
}
