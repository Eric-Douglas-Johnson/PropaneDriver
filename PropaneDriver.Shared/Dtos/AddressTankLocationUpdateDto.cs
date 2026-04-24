using System.ComponentModel.DataAnnotations;

namespace PropaneDriver.Shared.Dtos
{
    // Request body for PUT api/addresses/{id}/tank-location. Separate
    // from the coordinates DTO because the two fields change on very
    // different cadences and from different UX surfaces (the coord DTO
    // is driven by GPS; this one is a human note typed in by the driver
    // from the Navigation page).
    public class AddressTankLocationUpdateDto
    {
        [MaxLength(500)]
        public string? TankLocation { get; set; }
    }
}
