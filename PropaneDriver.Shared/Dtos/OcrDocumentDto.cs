namespace PropaneDriver.Shared.Dtos
{
    public class OcrDocumentDto
    {
        public string FileName { get; set; } = string.Empty;
        public int PageCount { get; set; }
        public List<OcrPageDto> Pages { get; set; } = new();

        // Populated only when the caller ran the file through the prebuilt-invoice
        // model. For other models (Read, Layout, etc.) this stays null since the
        // Azure response carries no structured invoice fields.
        public InvoiceData? Invoice { get; set; }
    }
}
