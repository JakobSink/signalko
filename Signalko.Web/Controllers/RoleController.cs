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

    // GET /api/Role/my-permissions
    // Always fetches user's CURRENT role from DB — JWT roleId can be stale after role change
    [HttpGet("my-permissions"), Authorize]
    public async Task<IActionResult> GetMyPermissions()
    {
        var uid = GetUserId();
        if (uid == null) return Ok(Array.Empty<string>());

        // Get current RoleId from DB (never trust stale JWT)
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

    // GET /api/Role
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
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
        if (!await IsAdminAsync()) return StatusCode(403, new { message = "Samo admin lahko upravlja vloge." });
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Ime je obvezno." });
        if (await _db.Roles.AnyAsync(r => r.Name == dto.Name))
            return Conflict(new { message = "Vloga s tem imenom že obstaja." });

        var role = new UserRole { Name = dto.Name.Trim() };
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
        if (!await IsAdminAsync()) return StatusCode(403, new { message = "Samo admin lahko upravlja vloge." });
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.id == id);
        if (role == null) return NotFound();
        if (role.Name == "Admin") return BadRequest(new { message = "Pravice vloge Admin ni mogoče urejati." });

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
        if (!await IsAdminAsync()) return StatusCode(403, new { message = "Samo admin lahko upravlja vloge." });
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

    private int? GetUserId()
    {
        // Try all possible locations for the user ID claim
        var raw = User.Identity?.Name                                                   // "sub" via NameClaimType="sub"
               ?? User.FindFirst("sub")?.Value
               ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }

    // Bulletproof admin check: JWT first (fast), DB second (stale token fallback)
    private async Task<bool> IsAdminAsync()
    {
        // 1. JWT: check every possible location for the "Admin" role claim
        if (User.IsInRole("Admin")) return true;
        if (User.Claims.Any(c =>
            (c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role)
            && c.Value == "Admin"))
            return true;

        // 2. DB: always authoritative (handles legacy accounts or stale tokens)
        var uid = GetUserId();
        if (uid == null)
        {
            Console.WriteLine("[Auth] IsAdminAsync: no user ID in token");
            return false;
        }

        var roleName = await _db.users
            .Where(u => u.id == uid)
            .Select(u => u.Role!.Name)
            .FirstOrDefaultAsync();

        Console.WriteLine($"[Auth] IsAdminAsync: uid={uid} roleName={roleName ?? "null"}");
        return roleName == "Admin";
    }

    private async Task SetPermissionsAsync(int roleId, IEnumerable<string> codes)
    {
        var existing = await _db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync();
        _db.RolePermissions.RemoveRange(existing);

        var codeList = codes.ToList();
        if (codeList.Count > 0)
        {
            var permIds = await _db.Permissions
                .Where(p => codeList.Contains(p.Code))
                .Select(p => p.id)
                .ToListAsync();

            foreach (var pid in permIds)
                _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = pid });
        }

        await _db.SaveChangesAsync();
    }
}

public record RoleWriteDto(string? Name, IEnumerable<string>? Permissions);
