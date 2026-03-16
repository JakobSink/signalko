using System.ComponentModel.DataAnnotations;

namespace Signalko.Web.DTOs;

public class SignupRequest
{
    [Required] public string Name { get; set; } = "";
    public string? Surname { get; set; }

    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, MinLength(6)] public string Password { get; set; } = "";

    [Required] public string LicenseKey { get; set; } = "";
}

public class LoginRequest
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required] public string Password { get; set; } = "";
}

public class AuthResponse
{
    public string  token   { get; set; } = "";
    public int     id      { get; set; }
    public string  cardID  { get; set; } = "";
    public string  name    { get; set; } = "";
    public string? surname { get; set; }
    public string  email   { get; set; } = "";
    public int?    roleId  { get; set; }
    public string? role    { get; set; }
}
