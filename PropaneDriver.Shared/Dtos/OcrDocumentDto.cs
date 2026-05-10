namespace PropaneDriver.Shared.Dtos
{
    public class OcrDocumentDto
    {
        public string FileName { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public List<OcrPageDto> Pages { get; set; } = new();
    }
}
