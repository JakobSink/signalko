using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/Role")]
public class RoleController : ControllerBase
{
    private readonly AppDbContext _db;
    public RoleController(AppDbContext db) => _db = db;

    // GET /api/Role/permissions
    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions()
    {
        var perms = await _db.Permissions.AsNoTracking()
            .OrderBy(p => p.Category).ThenBy(p => p.Code)
            .ToListAsync();
        return Ok(perms);
    }

    // GET /api/Role
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .ToListAsync();

        return Ok(roles.Select(r => new
        {
            r.id,
            r.Name,
            IsSystem = r.Name == "Admin" || r.Name == "User",
            IsAdmin  = r.Name == "Admin",
            Permissions = r.RolePermissions
                .Where(rp => rp.Permission != null)
                .Select(rp => rp.Permission!.Code)
                .ToList()
        }));
    }

    // POST /api/Role
    [HttpPost, Authorize]
    public async Task<IActionResult> Create([FromBody] RoleWriteDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Ime je obvezno." });
        if (await _db.Roles.AnyAsync(r => r.Name == dto.Name))
            return Conflict(new { message = "Vloga s tem imenom že obstaja." });

        var role = new UserRole { Name = dto.Name.Trim() };
        _db.Roles.Add(role);
        await _db.SaveChangesAsync();

        await SetPermissionsAsync(role.id, dto.Permissions ?? []);

        return Ok(new
        {
            role.id, role.Name,
            IsSystem = false, IsAdmin = false,
            Permissions = dto.Permissions ?? (IEnumerable<string>)[]
        });
    }

    // PUT /api/Role/{id}
    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, [FromBody] RoleWriteDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.id == id);
        if (role == null) return NotFound();
        if (role.Name == "Admin") return BadRequest(new { message = "Pravice vloge Admin ni mogoče urejati." });

        if (!string.IsNullOrWhiteSpace(dto.Name) && role.Name != "User")
            role.Name = dto.Name.Trim();

        await SetPermissionsAsync(id, dto.Permissions ?? []);
        await _db.SaveChangesAsync();

        return Ok(new
        {
            role.id, role.Name,
            IsSystem = role.Name is "Admin" or "User",
            IsAdmin  = false,
            Permissions = dto.Permissions ?? (IEnumerable<string>)[]
        });
    }

    // DELETE /api/Role/{id}
    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await IsAdminAsync()) return Forbid();
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.id == id);
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
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        _db.RolePermissions.RemoveRange(existing);

        var codeList = codes.ToList();
        var permIds  = await _db.Permissions
            .Where(p => codeList.Contains(p.Code))
            .Select(p => p.id)
            .ToListAsync();

        foreach (var pid in permIds)
            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = pid });

        await _db.SaveChangesAsync();
    }

    private async Task<bool> IsAdminAsync()
    {
        var role = User.FindFirst("role")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role == "Admin") return true;
        var rid = User.FindFirst("roleId")?.Value;
        if (rid == "1") return true;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        if (int.TryParse(sub, out var userId))
        {
            var byId = await _db.users.AsNoTracking().Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.id == userId);
            if (byId?.Role?.Name == "Admin") return true;
        }
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                 ?? User.FindFirst("email")?.Value;
        if (!string.IsNullOrEmpty(email))
        {
            var byEmail = await _db.users.AsNoTracking().Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email == email);
            if (byEmail?.Role?.Name == "Admin") return true;
        }
        return false;
    }
}

public record RoleWriteDto(string? Name, IEnumerable<string>? Permissions);
