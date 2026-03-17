using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/Module")]
public class ModuleController : PermissionedController
{
    public ModuleController(AppDbContext db) : base(db) { }

    // GET /api/Module — all available modules
    [HttpGet, Authorize]
    public async Task<IActionResult> GetAll()
    {
        var modules = await _db.Modules.AsNoTracking().OrderBy(m => m.id).ToListAsync();
        return Ok(modules);
    }

    // GET /api/Module/my — modules enabled for current license
    [HttpGet("my"), Authorize]
    public async Task<IActionResult> GetMy()
    {
        var licId = GetLicenseId();
        if (!licId.HasValue) return Unauthorized();

        var enabledCodes = await _db.LicenseModules
            .AsNoTracking()
            .Where(lm => lm.LicenseId == licId.Value)
            .Select(lm => lm.ModuleCode)
            .ToListAsync();

        var modules = await _db.Modules.AsNoTracking().ToListAsync();

        return Ok(modules.Select(m => new
        {
            m.id, m.Code, m.Name, m.Description, m.Icon, m.IsCore,
            Enabled = enabledCodes.Contains(m.Code),
        }));
    }

    // POST /api/Module/{code}/enable
    [HttpPost("{code}/enable"), Authorize]
    public async Task<IActionResult> Enable(string code)
    {
        if (!await HasPermAsync("license.manage")) return Forbidden("license.manage");

        var licId = GetLicenseId();
        if (!licId.HasValue) return Unauthorized();

        if (!await _db.Modules.AnyAsync(m => m.Code == code))
            return NotFound(new { message = $"Modul '{code}' ne obstaja." });

        if (await _db.LicenseModules.AnyAsync(lm => lm.LicenseId == licId.Value && lm.ModuleCode == code))
            return Conflict(new { message = "Modul je že aktiviran." });

        _db.LicenseModules.Add(new LicenseModule
        {
            LicenseId       = licId.Value,
            ModuleCode      = code,
            EnabledAt       = DateTime.UtcNow,
            EnabledByUserId = GetUserId(),
        });
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Modul '{code}' aktiviran." });
    }

    // DELETE /api/Module/{code}
    [HttpDelete("{code}"), Authorize]
    public async Task<IActionResult> Disable(string code)
    {
        if (!await HasPermAsync("license.manage")) return Forbidden("license.manage");

        var licId = GetLicenseId();
        if (!licId.HasValue) return Unauthorized();

        var module = await _db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Code == code);
        if (module == null) return NotFound();

        var lm = await _db.LicenseModules.FirstOrDefaultAsync(x => x.LicenseId == licId.Value && x.ModuleCode == code);
        if (lm == null) return NotFound(new { message = "Modul ni aktiviran." });

        _db.LicenseModules.Remove(lm);
        await _db.SaveChangesAsync();
        return Ok(new { message = $"Modul '{code}' deaktiviran." });
    }
}
