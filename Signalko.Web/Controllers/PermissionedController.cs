using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;

namespace Signalko.Web.Controllers;

/// <summary>
/// Base controller with permission checking via DB.
/// All controllers that need access control should extend this.
/// </summary>
public class PermissionedController : ControllerBase
{
    protected readonly AppDbContext _db;

    public PermissionedController(AppDbContext db) => _db = db;

    /// <summary>Extracts user ID from JWT (tries sub + ClaimTypes.NameIdentifier).</summary>
    protected int? GetUserId()
    {
        foreach (var c in User.Claims)
            if (c.Type == "sub" || c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)
                if (int.TryParse(c.Value, out var id)) return id;
        if (int.TryParse(User.Identity?.Name, out var n)) return n;
        return null;
    }

    /// <summary>Extracts the tenant LicenseId from the "lid" JWT claim.</summary>
    protected int? GetLicenseId()
    {
        var raw = User.Claims.FirstOrDefault(c => c.Type == "lid")?.Value;
        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Checks if the current user has a specific permission code.
    /// Always queries DB (fresh roleId + role_permissions check).
    /// </summary>
    protected async Task<bool> HasPermAsync(string code)
    {
        var uid = GetUserId();
        if (uid == null) return false;

        var roleId = await _db.users.AsNoTracking()
            .Where(u => u.id == uid)
            .Select(u => u.RoleId)
            .FirstOrDefaultAsync();

        if (roleId == null) return false;

        return await _db.RolePermissions
            .AnyAsync(rp => rp.RoleId == roleId && rp.Permission!.Code == code);
    }

    protected IActionResult Forbidden(string code) =>
        StatusCode(403, new { message = $"Nimaš dovoljenja: {code}" });
}
