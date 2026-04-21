namespace PropaneDriver.Shared.Dtos
{
    public class GeocodingResultDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string DisplayName { get; set; } = string.Empty;

        // Parsed address fields. Populated whether the result came from the
        // Addresses table or from Google (address_components parsing), so the
        // Admin form can auto-fill City/State/Zip without a second round-trip.
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;

        // "Database" when served from the Addresses table, "Google" when
        // geocoded fresh. Surfaced in the UI so the operator knows whether
        // they're looking at saved data or a new lookup.
        public string Source { get; set; } = string.Empty;
    }
}
