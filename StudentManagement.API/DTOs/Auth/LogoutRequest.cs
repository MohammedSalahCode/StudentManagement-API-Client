namespace StudentManagement.API.DTOs.Auth
{
    public class LogoutRequest
    {
        public string Email { get; set; }
        public string RefreshToken { get; set; }
    }
}
