namespace PropaneDriver.Shared.Dtos
{
    // Request body for the admin-only POST api/drivers. Role must be one of UserRoles.All.
    public class CreateDriverDto : RegisterDriverDto
    {
        public string Role { get; set; } = string.Empty;
    }
}
