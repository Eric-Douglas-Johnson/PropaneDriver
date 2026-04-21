namespace PropaneDriver.Shared.Dtos
{
    // Request body for PUT api/addresses/{id}/coordinates. Kept as a
    // minimal two-field payload because the "set truck position as
    // address pin" UX only needs lat/lon — the rest of the row stays.
    public class AddressCoordinatesUpdateDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }
}
