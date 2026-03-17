using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Controllers;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/SuperAdmin")]
public class SuperAdminController : ControllerBase
{
    private readonly AppDbContext    _db;
    private readonly IConfiguration  _cfg;

    public SuperAdminController(AppDbContext db, IConfiguration cfg)
    {
        _db  = db;
        _cfg = cfg;
    }

    // Every endpoint calls this first.
    private IActionResult? CheckSecret()
    {
        var expected = _cfg["SuperAdmin:Secret"];
        if (string.IsNullOrWhiteSpace(expected) || expected == "REPLACE_WITH_STRONG_SUPERADMIN_SECRET")
            return StatusCode(503, new { message = "SuperAdmin secret is not configured." });

        var provided = Request.Headers["X-SK-Secret"].FirstOrDefault();
        if (provided != expected)
            return StatusCode(401, new { message = "Invalid secret." });

        return null; // OK
    }

    // GET /api/SuperAdmin/licenses
    [HttpGet("licenses")]
    public async Task<IActionResult> GetLicenses()
    {
        if (CheckSecret() is { } err) return err;

        var licenses = await _db.Licenses.AsNoTracking().OrderBy(l => l.id).ToListAsync();

        var result = new List<object>();
        foreach (var lic in licenses)
        {
            var activeUsers  = await _db.users.CountAsync(u => u.IsActive   && u.LicenseId == lic.id);
            var totalUsers   = await _db.users.CountAsync(u =>                  u.LicenseId == lic.id);
            var enabledMods  = await _db.LicenseModules.AsNoTracking()
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
        if (CheckSecret() is { } err) return err;

        var key = LicenseController.GenerateLicenseKey();
        var lic = new License
        {
            LicenseKey       = key,
            CompanyName      = dto.CompanyName?.Trim(),
            MaxUsers         = dto.MaxUsers         > 0 ? dto.MaxUsers         : 10,
            MaxReadingPoints = dto.MaxReadingPoints  > 0 ? dto.MaxReadingPoints  : 5,
            CreatedAt        = DateTime.UtcNow,
            UpdatedAt        = DateTime.UtcNow,
        };
        _db.Licenses.Add(lic);
        await _db.SaveChangesAsync();

        // Auto-enable core modules for the new license
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
        if (CheckSecret() is { } err) return err;

        var lic = await _db.Licenses.FirstOrDefaultAsync(l => l.id == id);
        if (lic == null) return NotFound();

        if (dto.CompanyName      != null) lic.CompanyName      = dto.CompanyName.Trim();
        if (dto.MaxUsers         >  0   ) lic.MaxUsers         = dto.MaxUsers;
        if (dto.MaxReadingPoints >  0   ) lic.MaxReadingPoints  = dto.MaxReadingPoints;
        lic.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new { lic.id, lic.LicenseKey, lic.CompanyName, lic.MaxUsers, lic.MaxReadingPoints });
    }

    // GET /api/SuperAdmin/modules
    [HttpGet("modules")]
    public async Task<IActionResult> GetModules()
    {
        if (CheckSecret() is { } err) return err;
        return Ok(await _db.Modules.AsNoTracking().OrderBy(m => m.id).ToListAsync());
    }

    // POST /api/SuperAdmin/licenses/{id}/modules/{code}  — enable module
    [HttpPost("licenses/{id}/modules/{code}")]
    public async Task<IActionResult> EnableModule(int id, string code)
    {
        if (CheckSecret() is { } err) return err;

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

    // DELETE /api/SuperAdmin/licenses/{id}/modules/{code}  — disable module
    [HttpDelete("licenses/{id}/modules/{code}")]
    public async Task<IActionResult> DisableModule(int id, string code)
    {
        if (CheckSecret() is { } err) return err;

        var lm = await _db.LicenseModules
            .FirstOrDefaultAsync(x => x.LicenseId == id && x.ModuleCode == code);
        if (lm == null) return NotFound(new { message = "Modul ni aktiviran." });

        _db.LicenseModules.Remove(lm);
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Modul '{code}' deaktiviran za licenco {id}." });
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────
public record SuperAdminLicenseCreateDto(string? CompanyName, int MaxUsers, int MaxReadingPoints);
public record SuperAdminLicenseUpdateDto(string? CompanyName, int MaxUsers, int MaxReadingPoints);
