using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PresenceController : PermissionedController
{
    public PresenceController(AppDbContext db) : base(db) {}

    // ── GET /api/presence/current — kdo je zdaj znotraj ─────────────────────
    [HttpGet("current"), Authorize]
    public async Task<IActionResult> Current()
    {
        if (!await HasPermAsync("presence.manage")) return Forbidden("presence.manage");

        var licId = GetLicenseId();
        var licFilter = licId.HasValue ? $"AND u.LicenseId = {licId.Value}" : "";
        var sql = $@"
            SELECT u.id, u.Name, u.Surname, u.CardID, u.CardEpc,
                   p.Type, p.ScannedAt, z.Name AS ZoneName
            FROM users u
            JOIN user_presence p ON p.id = (
                SELECT id FROM user_presence
                WHERE UserId = u.id
                ORDER BY ScannedAt DESC
                LIMIT 1
            )
            LEFT JOIN zones z ON z.id = p.ZoneId
            WHERE p.Type = 'IN' {licFilter}
            ORDER BY p.ScannedAt DESC;";

        await using var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var result = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            result.Add(new
            {
                id        = (int)rdr["id"],
                name      = rdr["Name"]     as string,
                surname   = rdr["Surname"]  as string,
                cardId    = rdr["CardID"]   as string,
                cardEpc   = rdr["CardEpc"]  as string,
                type      = rdr["Type"]     as string,
                scannedAt = rdr["ScannedAt"] is DBNull ? null : (DateTime?)rdr["ScannedAt"],
                zone      = rdr["ZoneName"] as string,
            });
        }

        return Ok(result);
    }

    // ── GET /api/presence/log — vsi eventi, paginirani ───────────────────────
    [HttpGet("log"), Authorize]
    public async Task<IActionResult> Log(
        [FromQuery] int?      userId   = null,
        [FromQuery] string?   search   = null,
        [FromQuery] string?   cardId   = null,
        [FromQuery] string?   type     = null,
        [FromQuery] DateTime? from     = null,
        [FromQuery] DateTime? to       = null,
        [FromQuery] int       page     = 1,
        [FromQuery] int       pageSize = 50)
    {
        if (!await HasPermAsync("presence.manage")) return Forbidden("presence.manage");

        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var licId = GetLicenseId();
        var q = _db.UserPresences.AsNoTracking().Include(p => p.User).Include(p => p.Zone)
            .Where(p => p.User != null && p.User.LicenseId == licId).AsQueryable();

        if (userId.HasValue) q = q.Where(p => p.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(cardId))
            q = q.Where(p => p.User != null && p.User.CardID != null && p.User.CardID.Contains(cardId));
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(p => p.User != null &&
                (p.User.Name.Contains(search) || p.User.Surname != null && p.User.Surname.Contains(search)));
        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(p => p.Type == type.ToUpper());
        if (from.HasValue) q = q.Where(p => p.ScannedAt >= from.Value);
        if (to.HasValue)   q = q.Where(p => p.ScannedAt <= to.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.ScannedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                id        = p.id,
                userId    = p.UserId,
                name      = p.User != null ? (p.User.Name + " " + p.User.Surname).Trim() : null,
                cardId    = p.User != null ? p.User.CardID : null,
                type      = p.Type,
                scannedAt = p.ScannedAt,
                zone      = p.Zone != null ? p.Zone.Name : null,
                zoneType  = p.Zone != null ? p.Zone.Type : null,
            })
            .ToListAsync();

        return Ok(new { page, pageSize, total, items });
    }

    // ── GET /api/presence/me — lastni dogodki prijavljenega userja ────────────
    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me(
        [FromQuery] string?   type     = null,
        [FromQuery] DateTime? from     = null,
        [FromQuery] DateTime? to       = null,
        [FromQuery] int       page     = 1,
        [FromQuery] int       pageSize = 50)
    {
        var uid = GetUserId();
        if (uid == null) return Unauthorized();

        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var q = _db.UserPresences.AsNoTracking().Where(p => p.UserId == uid.Value).Include(p => p.Zone).AsQueryable();

        if (!string.IsNullOrWhiteSpace(type)) q = q.Where(p => p.Type == type.ToUpper());
        if (from.HasValue) q = q.Where(p => p.ScannedAt >= from.Value);
        if (to.HasValue)   q = q.Where(p => p.ScannedAt <= to.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.ScannedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                id        = p.id,
                type      = p.Type,
                scannedAt = p.ScannedAt,
                zone      = p.Zone != null ? p.Zone.Name : null,
                zoneType  = p.Zone != null ? p.Zone.Type : null,
            })
            .ToListAsync();

        return Ok(new { page, pageSize, total, items });
    }

    // ── GET /api/presence/user/{id} — dogodki za konkretnega userja ──────────
    [HttpGet("user/{id:int}"), Authorize]
    public async Task<IActionResult> UserLog(int id,
        [FromQuery] DateTime? from     = null,
        [FromQuery] DateTime? to       = null,
        [FromQuery] int       page     = 1,
        [FromQuery] int       pageSize = 50)
    {
        if (!await HasPermAsync("presence.manage")) return Forbidden("presence.manage");

        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var licId = GetLicenseId();
        var q = _db.UserPresences.AsNoTracking().Where(p => p.UserId == id).Include(p => p.Zone).AsQueryable();

        if (from.HasValue) q = q.Where(p => p.ScannedAt >= from.Value);
        if (to.HasValue)   q = q.Where(p => p.ScannedAt <= to.Value);

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(p => p.ScannedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                id        = p.id,
                type      = p.Type,
                scannedAt = p.ScannedAt,
                zone      = p.Zone != null ? p.Zone.Name : null,
                zoneType  = p.Zone != null ? p.Zone.Type : null,
            })
            .ToListAsync();

        var user = await _db.users.AsNoTracking()
            .Where(u => u.id == id && u.LicenseId == licId)
            .Select(u => new { u.id, u.Name, u.Surname, u.CardID, u.CardEpc })
            .FirstOrDefaultAsync();

        if (user == null) return NotFound();

        return Ok(new { user, page, pageSize, total, items });
    }
}
