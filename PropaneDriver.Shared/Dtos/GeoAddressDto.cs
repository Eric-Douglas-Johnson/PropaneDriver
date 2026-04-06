namespace PropaneDriver.Shared.Dtos
{
    public class GeoAddressDto
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public string FullAddress =>
            string.Join(", ", new[] { Street, City, State, ZipCode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        public bool HasCoordinates => Latitude != 0 || Longitude != 0;
    }
}
