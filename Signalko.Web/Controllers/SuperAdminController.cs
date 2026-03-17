using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/SuperAdmin")]
public class SuperAdminController : ControllerBase
{
    private readonly AppDbContext     _db;
    private readonly IConfiguration   _cfg;
    private readonly JwtTokenService  _jwt;

    public SuperAdminController(AppDbContext db, IConfiguration cfg, JwtTokenService jwt)
    {
        _db  = db;
        _cfg = cfg;
        _jwt = jwt;
    }

    // ── Auth helpers ─────────────────────────────────────────────────────────

    // POST /api/SuperAdmin/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] SuperAdminLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Email in geslo sta obvezna." });

        var user = await _db.SuperAdminUsers
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower());

        if (user == null || !PasswordHasher.Verify(dto.Password, user.PasswordHash))
            return StatusCode(401, new { message = "Napačen email ali geslo." });

        var token = _jwt.CreateSuperAdminToken(user.id, user.Email);
        return Ok(new { token });
    }

    // Validates X-SK-Token header: must be a valid JWT with claim sa=true
    private IActionResult? CheckToken()
    {
        var raw = Request.Headers["X-SK-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
            return StatusCode(401, new { message = "Manjka X-SK-Token." });

        var key = _cfg["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            return StatusCode(503, new { message = "JWT ključ ni nastavljen." });

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(raw, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ValidateIssuer           = false,
                ValidateAudience         = false,
                ClockSkew                = TimeSpan.Zero,
            }, out _);

            if (principal.FindFirst("sa")?.Value != "true")
                return StatusCode(403, new { message = "Nisi SuperAdmin." });

            return null; // OK
        }
        catch
        {
            return StatusCode(401, new { message = "Neveljaven ali potekel token." });
        }
    }

    // ── Licenses ─────────────────────────────────────────────────────────────

    // GET /api/SuperAdmin/licenses
    [HttpGet("licenses")]
    public async Task<IActionResult> GetLicenses()
    {
        if (CheckToken() is { } err) return err;

        var licenses = await _db.Licenses.AsNoTracking().OrderBy(l => l.id).ToListAsync();

        var result = new List<object>();
        foreach (var lic in licenses)
        {
            var activeUsers = await _db.users.CountAsync(u => u.IsActive && u.LicenseId == lic.id);
            var totalUsers  = await _db.users.CountAsync(u => u.LicenseId == lic.id);
            var enabledMods = await _db.LicenseModules.AsNoTracking()
                                  .Where(lm => lm.LicenseId == lic.id)
                                  .Select(lm => lm.ModuleCode)
                                  .ToListAsync();

            result.Add(new
            {
                lic.id, lic.LicenseKey, lic.CompanyName,
                lic.MaxUsers, lic.MaxReadingPoints,
                lic.CreatedAt, lic.UpdatedAt, lic.ActivatedAt,
                activeUsers, totalUsers,
                enabledModules = enabledMods,
                isActivated = lic.ActivatedAt.HasValue,
            });
        }

        return Ok(result);
    }

    // POST /api/SuperAdmin/licenses
    [HttpPost("licenses")]
    public async Task<IActionResult> CreateLicense([FromBody] SuperAdminLicenseCreateDto dto)
    {
        if (CheckToken() is { } err) return err;

        var key = LicenseController.GenerateLicenseKey();
        var lic = new License
        {
            LicenseKey       = key,
            CompanyName      = dto.CompanyName?.Trim(),
            MaxUsers         = dto.MaxUsers         > 0 ? dto.MaxUsers         : 10,
            MaxReadingPoints = dto.MaxReadingPoints  > 0 ? dto.MaxReadingPoints : 5,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };
        _db.Licenses.Add(lic);
        await _db.SaveChangesAsync();

        var coreCodes = await _db.Modules.Where(m => m.IsCore).Select(m => m.Code).ToListAsync();
        foreach (var code in coreCodes)
            _db.LicenseModules.Add(new LicenseModule { LicenseId = lic.id, ModuleCode = code, EnabledAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        return Ok(new { lic.id, lic.LicenseKey, lic.CompanyName, lic.MaxUsers, lic.MaxReadingPoints });
    }

    // PUT /api/SuperAdmin/licenses/{id}
    [HttpPut("licenses/{id}")]
    public async Task<IActionResult> UpdateLicense(int id, [FromBody] SuperAdminLicenseUpdateDto dto)
    {
        if (CheckToken() is { } err) return err;

        var lic = await _db.Licenses.FirstOrDefaultAsync(l => l.id == id);
        if (lic == null) return NotFound();

        if (dto.CompanyName      != null) lic.CompanyName      = dto.CompanyName.Trim();
        if (dto.MaxUsers         >  0   ) lic.MaxUsers         = dto.MaxUsers;
        if (dto.MaxReadingPoints >  0   ) lic.MaxReadingPoints = dto.MaxReadingPoints;
        lic.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { lic.id, lic.LicenseKey, lic.CompanyName, lic.MaxUsers, lic.MaxReadingPoints });
    }

    // ── Modules ──────────────────────────────────────────────────────────────

    // GET /api/SuperAdmin/modules
    [HttpGet("modules")]
    public async Task<IActionResult> GetModules()
    {
        if (CheckToken() is { } err) return err;
        return Ok(await _db.Modules.AsNoTracking().OrderBy(m => m.id).ToListAsync());
    }

    // POST /api/SuperAdmin/licenses/{id}/modules/{code}
    [HttpPost("licenses/{id}/modules/{code}")]
    public async Task<IActionResult> EnableModule(int id, string code)
    {
        if (CheckToken() is { } err) return err;

        if (!await _db.Licenses.AnyAsync(l => l.id == id))
            return NotFound(new { message = "Licenca ne obstaja." });
        if (!await _db.Modules.AnyAsync(m => m.Code == code))
            return NotFound(new { message = $"Modul '{code}' ne obstaja." });
        if (await _db.LicenseModules.AnyAsync(lm => lm.LicenseId == id && lm.ModuleCode == code))
            return Conflict(new { message = "Modul je že aktiviran." });

        _db.LicenseModules.Add(new LicenseModule
        {
            LicenseId  = id,
            ModuleCode = code,
            EnabledAt  = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Modul '{code}' aktiviran za licenco {id}." });
    }

    // DELETE /api/SuperAdmin/licenses/{id}/modules/{code}
    [HttpDelete("licenses/{id}/modules/{code}")]
    public async Task<IActionResult> DisableModule(int id, string code)
    {
        if (CheckToken() is { } err) return err;

        var lm = await _db.LicenseModules
            .FirstOrDefaultAsync(x => x.LicenseId == id && x.ModuleCode == code);
        if (lm == null) return NotFound(new { message = "Modul ni aktiviran." });

        _db.LicenseModules.Remove(lm);
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Modul '{code}' deaktiviran za licenco {id}." });
    }

    // ── SuperAdmin user management ────────────────────────────────────────────

    // GET /api/SuperAdmin/users
    [HttpGet("users")]
    public async Task<IActionResult> GetSuperAdminUsers()
    {
        if (CheckToken() is { } err) return err;
        var users = await _db.SuperAdminUsers.AsNoTracking()
            .Select(u => new { u.id, u.Email, u.CreatedAt })
            .ToListAsync();
        return Ok(users);
    }

    // POST /api/SuperAdmin/users
    [HttpPost("users")]
    public async Task<IActionResult> CreateSuperAdminUser([FromBody] SuperAdminCreateUserDto dto)
    {
        if (CheckToken() is { } err) return err;
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest(new { message = "Email in geslo sta obvezna." });

        var email = dto.Email.Trim().ToLower();
        if (await _db.SuperAdminUsers.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "Ta email že obstaja." });

        _db.SuperAdminUsers.Add(new SuperAdminUser
        {
            Email        = email,
            PasswordHash = PasswordHasher.Hash(dto.Password),
            CreatedAt    = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = $"SuperAdmin uporabnik {email} ustvarjen." });
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────
public record SuperAdminLoginDto(string Email, string Password);
public record SuperAdminLicenseCreateDto(string? CompanyName, int MaxUsers, int MaxReadingPoints);
public record SuperAdminLicenseUpdateDto(string? CompanyName, int MaxUsers, int MaxReadingPoints);
public record SuperAdminCreateUserDto(string Email, string Password);
