namespace PropaneDriver.Shared.Dtos
{
    public class GeoFenceEventArgs
    {
        public string DeliveryId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsInsideFence { get; set; }
        public double DistanceMeters { get; set; }
    }
}
