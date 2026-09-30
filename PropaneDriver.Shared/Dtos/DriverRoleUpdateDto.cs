namespace PropaneDriver.Shared.Dtos
{
    // Request body for PUT api/drivers/{id}/role. Must be one of UserRoles.All.
    public class DriverRoleUpdateDto
    {
        public string Role { get; set; } = string.Empty;
    }
}
