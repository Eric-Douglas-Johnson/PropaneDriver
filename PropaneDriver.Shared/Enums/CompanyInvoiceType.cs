namespace PropaneDriver.Shared.Enums
{
    // Identifies which company-specific invoice schema a scanned invoice
    // should be parsed under. Generic keeps the standard InvoiceData shape;
    // vendor-specific values map to derived classes (e.g. HooverInvoiceData)
    // that layer extra fields on top of the base invoice schema.
    public enum CompanyInvoiceType
    {
        Generic,
        Hoover
    }
}
