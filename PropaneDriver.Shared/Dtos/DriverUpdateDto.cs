namespace PropaneDriver.Shared.Dtos
{
    // Request body for the admin-only PUT api/drivers/{id}. A blank NewPassword keeps the current one.
    public class DriverUpdateDto
    {
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string MiddleName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
