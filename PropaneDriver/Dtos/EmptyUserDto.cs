namespace PropaneDriver.Dtos
{
    public class EmptyUserDto : UserDto
    {
        public EmptyUserDto()
        {
            Id = string.Empty;
            Role = string.Empty;
            UserName = string.Empty;
            FirstName = string.Empty;
            MiddleName = string.Empty;
            LastName = string.Empty;
            Email = string.Empty;
            PhoneNumber = string.Empty;
        }
    }
}
