using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Signalko.Core;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Signalko.Web.Services;

public class JwtTokenService
{
    private readonly IConfiguration _cfg;

    public JwtTokenService(IConfiguration cfg) => _cfg = cfg;

    public string CreateToken(User u, string? roleName = null, int? expiresMinutesOverride = null, bool kiosk = false)
    {
        var key            = _cfg["Jwt:Key"]      ?? throw new Exception("Jwt:Key manjka v appsettings.json");
        var issuer         = _cfg["Jwt:Issuer"]   ?? "Signalko";
        var audience       = _cfg["Jwt:Audience"] ?? "Signalko";
        var expiresMinutes = expiresMinutesOverride
                             ?? (int.TryParse(_cfg["Jwt:ExpiresMinutes"], out var m) ? m : 240);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, u.id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, u.Email),
            new Claim("cardID", u.CardID ?? ""),
            new Claim("name",   u.Name   ?? ""),
            new Claim("lid",    u.LicenseId?.ToString() ?? ""),
        };

        // Role claims — use navigation prop name if available, fallback to parameter
        var role = roleName ?? u.Role?.Name;
        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim("role", role));
        if (u.RoleId.HasValue)
            claims.Add(new Claim("roleId", u.RoleId.Value.ToString()));
        if (kiosk)
            claims.Add(new Claim("kiosk", "true"));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds      = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             issuer,
            audience:           audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddMinutes(expiresMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
