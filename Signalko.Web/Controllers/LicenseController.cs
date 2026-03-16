using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Contracts;
using Signalko.Web.Services;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/License")]
public class LicenseController : PermissionedController
{
    public LicenseController(AppDbContext db) : base(db) { }

    // GET /api/License — anyone logged in with license.view can see it
    [HttpGet, Authorize]
    public async Task<IActionResult> Get()
    {
        if (!await HasPermAsync("license.view")) return Forbidden("license.view");

        var licId = GetLicenseId();
        var lic = licId.HasValue
            ? await _db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.id == licId.Value)
            : await _db.Licenses.AsNoTracking().FirstOrDefaultAsync();
        if (lic == null) return NotFound(new { message = "Licenca ni nastavljena." });

        var activeUsers = await _db.users.CountAsync(u => u.IsActive && u.LicenseId == lic.id);
        var totalUsers  = await _db.users.CountAsync(u => u.LicenseId == lic.id);

        return Ok(new LicenseDto(
            lic.id, lic.LicenseKey, lic.MaxUsers,
            activeUsers, totalUsers,
            lic.Domain, lic.CreatedAt, lic.UpdatedAt
        ));
    }

    // GET /api/License/check — lightweight check used internally (can add user?)
    [HttpGet("check"), Authorize]
    public async Task<IActionResult> Check()
    {
        var licId = GetLicenseId();
        var lic = licId.HasValue
            ? await _db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.id == licId.Value)
            : await _db.Licenses.AsNoTracking().FirstOrDefaultAsync();
        if (lic == null) return Ok(new { canAddUser = true, activeUsers = 0, maxUsers = 9999 });

        var activeUsers = await _db.users.CountAsync(u => u.IsActive && u.LicenseId == lic.id);
        return Ok(new { canAddUser = activeUsers < lic.MaxUsers, activeUsers, maxUsers = lic.MaxUsers });
    }

    internal static string GenerateLicenseKey()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars
        var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        string[] groups = new string[4];
        var usedGroups = new HashSet<string>();
        for (int i = 0; i < 4; i++)
        {
            string group;
            do
            {
                var bytes = new byte[4];
                rng.GetBytes(bytes);
                group = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
            } while (!usedGroups.Add(group));
            groups[i] = group;
        }
        return $"{groups[0]}-{groups[1]}-{groups[2]}-{groups[3]}";
    }
}
