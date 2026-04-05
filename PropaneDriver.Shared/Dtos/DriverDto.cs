namespace PropaneDriver.Shared.Dtos
{
    public class DriverDto : UserDto
    {
        public string LicenseClass { get; set; } = string.Empty;
        public string TruckNumber { get; set; } = string.Empty;
    }
}
