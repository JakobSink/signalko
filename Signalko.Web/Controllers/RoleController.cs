using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/Role")]
public class RoleController : PermissionedController
{
    public RoleController(AppDbContext db) : base(db) { }

    // GET /api/Role/my-permissions — fresh from DB, never from stale JWT
    [HttpGet("my-permissions"), Authorize]
    public async Task<IActionResult> GetMyPermissions()
    {
        var uid = GetUserId();
        if (uid == null) return Ok(Array.Empty<string>());

        var roleId = await _db.users.AsNoTracking()
            .Where(u => u.id == uid)
            .Select(u => u.RoleId)
            .FirstOrDefaultAsync();

        if (roleId == null) return Ok(Array.Empty<string>());

        var codes = await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission!.Code)
            .ToListAsync();

        return Ok(codes);
    }

    // GET /api/Role/permissions
    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions()
    {
        var perms = await _db.Permissions.AsNoTracking()
            .OrderBy(p => p.Category).ThenBy(p => p.Code)
            .ToListAsync();
        return Ok(perms.Select(p => new
        {
            id       = p.id,
            code     = p.Code,
            label    = p.Label,
            category = p.Category,
        }));
    }

    // GET /api/Role — returns system roles (LicenseId IS NULL) + this tenant's custom roles
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var licId = GetLicenseId();
        var roles = await _db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Where(r => r.LicenseId == null || r.LicenseId == licId)
            .ToListAsync();

        return Ok(roles.Select(r => new
        {
            id          = r.id,
            name        = r.Name,
            isSystem    = r.Name == "Admin" || r.Name == "User",
            isAdmin     = r.Name == "Admin",
            permissions = r.RolePermissions
                .Where(rp => rp.Permission != null)
                .Select(rp => rp.Permission!.Code)
                .ToList()
        }));
    }

    // POST /api/Role
    [HttpPost, Authorize]
    public async Task<IActionResult> Create([FromBody] RoleWriteDto dto)
    {
        if (!await HasPermAsync("roles.manage"))
            return StatusCode(403, new { message = "Nimaš dovoljenja za upravljanje vlog (roles.manage)." });
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "Ime je obvezno." });

        var licId = GetLicenseId();
        if (await _db.Roles.AnyAsync(r => r.Name == dto.Name && (r.LicenseId == null || r.LicenseId == licId)))
            return Conflict(new { message = "Vloga s tem imenom že obstaja." });

        var role = new UserRole { Name = dto.Name.Trim(), LicenseId = licId };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        await SetPermissionsAsync(role.id, dto.Permissions ?? []);

        return Ok(new
        {
            id          = role.id,
            name        = role.Name,
            isSystem    = false,
            isAdmin     = false,
            permissions = dto.Permissions ?? (IEnumerable<string>)[]
        });
    }

    // PUT /api/Role/{id}
    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, [FromBody] RoleWriteDto dto)
    {
        if (!await HasPermAsync("roles.manage"))
            return StatusCode(403, new { message = "Nimaš dovoljenja za upravljanje vlog (roles.manage)." });

        var licId = GetLicenseId();
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.id == id && (r.LicenseId == null || r.LicenseId == licId));
        if (role == null) return NotFound();
        if (role.Name == "Admin")
            return BadRequest(new { message = "Pravice vloge Admin ni mogoče urejati." });

        if (!string.IsNullOrWhiteSpace(dto.Name) && role.Name != "User")
            role.Name = dto.Name.Trim();

        await _db.SaveChangesAsync();
        await SetPermissionsAsync(id, dto.Permissions ?? []);

        return Ok(new
        {
            id          = role.id,
            name        = role.Name,
            isSystem    = role.Name is "Admin" or "User",
            isAdmin     = false,
            permissions = dto.Permissions ?? (IEnumerable<string>)[]
        });
    }

    // DELETE /api/Role/{id}
    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasPermAsync("roles.manage"))
            return StatusCode(403, new { message = "Nimaš dovoljenja za upravljanje vlog (roles.manage)." });

        var licId = GetLicenseId();
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.id == id && (r.LicenseId == null || r.LicenseId == licId));
        if (role == null) return NotFound();
        if (role.Name is "Admin" or "User")
            return BadRequest(new { message = "Sistemske vloge ni mogoče izbrisati." });
        if (await _db.users.AnyAsync(u => u.RoleId == id))
            return Conflict(new { message = "Vloga je dodeljena uporabnikom — najprej jo zamenjaj." });

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Vloga izbrisana." });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task SetPermissionsAsync(int roleId, IEnumerable<string> codes)
    {
        // Remove all existing permissions for this role
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        _db.RolePermissions.RemoveRange(existing);

        var codeList = codes.ToList();
        if (codeList.Count > 0)
        {
            // Look up permission IDs by code, then insert rows into role_permissions
            var permIds = await _db.Permissions
                .Where(p => codeList.Contains(p.Code))
                .Select(p => p.id)
                .ToListAsync();

            foreach (var pid in permIds)
                _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = pid });
        }

        await _db.SaveChangesAsync();
        Console.WriteLine($"[Roles] SetPermissions: roleId={roleId} codes=[{string.Join(",", codeList)}]");
    }
}

public record RoleWriteDto(string? Name, IEnumerable<string>? Permissions);
