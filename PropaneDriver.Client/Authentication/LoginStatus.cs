namespace PropaneDriver.Client.Authentication
{
    public class LoginStatus
    {
        public bool Successful { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
