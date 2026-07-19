namespace PropaneDriver.Shared.Dtos
{
    public class AddressDto
    {
        public Guid Id { get; set; }
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double AvgDeliveryTimeMinutes { get; set; }

        // Optional free-text note about where the tank sits on the
        // property. Null until the admin fills it in.
        public string? TankLocation { get; set; }

        // True when the driver must back the truck into this driveway.
        public bool BackIn { get; set; }

        public string FullAddress =>
            string.Join(", ", new[] { Street, City, State, ZipCode }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        public bool HasCoordinates => Latitude != 0 || Longitude != 0;
    }
}
