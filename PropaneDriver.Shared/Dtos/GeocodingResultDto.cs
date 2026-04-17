namespace PropaneDriver.Shared.Dtos
{
    public class GeocodingResultDto
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
