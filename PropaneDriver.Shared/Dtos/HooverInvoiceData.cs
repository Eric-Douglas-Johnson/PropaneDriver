namespace PropaneDriver.Shared.Dtos
{
    // Vendor-specific extension of InvoiceData for Hoover invoices.
    // Inherits every standard invoice field from the base class and layers
    // on the per-equipment fuel breakdown that Hoover invoices itemize
    // alongside the usual line items.
    public class HooverInvoiceData : InvoiceData
    {
        public List<EquipmentPiece> EquipmentPieces { get; set; } = new();
    }

    public class EquipmentPiece
    {
        public int Id { get; set; }
        public decimal FuelQuantity { get; set; }
    }
}
