namespace Signalko.Web.Contracts;

// 🇸🇮 Request za registracijo
public class SignupRequest
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

// 🇸🇮 Request za prijavo
public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

// 🇸🇮 Odgovor (JWT token)
public class AuthResponse
{
    public string Token { get; set; } = "";
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
