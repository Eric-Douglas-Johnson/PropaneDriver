using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Shared.Dtos
{
    // One row of a driver's fuel log: which piece of equipment was fueled,
    // the fuel-pump meter reading at that fill, and the gallons that fill
    // dispensed. GallonsPumped is normally derived on the client as
    // (this row's meter − the previous row's meter), but it's stored so
    // reports don't have to recompute it.
    public class FuelLogEntryDto
    {
        // Empty for a row that hasn't been persisted yet.
        public string Id { get; set; } = string.Empty;

        [MaxLength(100)]
        public string EquipmentNumber { get; set; } = string.Empty;

        public decimal MeterValue { get; set; }

        public decimal GallonsPumped { get; set; }

        // Position within the log. The gallons-pumped calculation is
        // order-dependent, so this is preserved on save and used to order
        // rows on load.
        public int SortOrder { get; set; }

        public DateTime RecordedAt { get; set; }
    }

    // Body of the "save" request — replaces the signed-in driver's entire
    // fuel log with this ordered set of rows.
    public class SaveFuelLogDto
    {
        public List<FuelLogEntryDto> Entries { get; set; } = new();
    }
}
