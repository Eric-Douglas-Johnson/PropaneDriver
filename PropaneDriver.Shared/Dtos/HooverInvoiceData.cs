namespace PropaneDriver.Shared.Dtos
{
    // Vendor-specific extension of InvoiceData for Hoover invoices.
    // Inherits every standard invoice field from the base class and layers
    // on the per-equipment fuel breakdown that Hoover invoices itemize
    // alongside the usual line items.
    public class HooverInvoiceData : InvoiceData
    {
        public List<EquipmentPiece> EquipmentPieces { get; set; } = new();

        // Total fuel pumped across the run. On a Hoover equipment sheet this
        // is the trailing number that follows the per-piece id/quantity
        // pairs (just before the HOOVER SUPERVISOR sign-off line), so it
        // arrives from the same OCR pass that fills EquipmentPieces.
        public decimal? TotalFuelPumped { get; set; }
    }

    public class EquipmentPiece
    {
        // Stored as the raw digit string straight from OCR so leading zeros
        // (e.g. "00123") survive round-tripping into Excel — parsing through
        // int/long would silently strip them.
        public string Id { get; set; } = string.Empty;
        public decimal FuelQuantity { get; set; }
    }
}
