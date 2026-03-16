using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Core;
using Signalko.Web.Contracts;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AntennaController : PermissionedController
{
    public AntennaController(AppDbContext db) : base(db) {}

    private async Task<AntennaDto> MapToDtoAsync(Antenna a)
    {
        var role = await _db.Role.AsNoTracking().FirstOrDefaultAsync(r => r.id == a.RoleID);
        return new AntennaDto(a.id, a.ReaderId, a.Port, a.ZoneId, a.RoleID, role?.Name);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? readerId)
    {
        var licId = GetLicenseId();
        IQueryable<Antenna> q = _db.antennas.AsNoTracking().Include(a => a.Reader)
            .Where(a => a.Reader != null && a.Reader.LicenseId == licId);
        if (readerId.HasValue) q = q.Where(a => a.ReaderId == readerId.Value);
        var list = await q.OrderBy(a => a.ReaderId).ThenBy(a => a.Port).ToListAsync();
        var result = new List<AntennaDto>(list.Count);
        foreach (var a in list) result.Add(await MapToDtoAsync(a));
        return Ok(result);
    }

    [HttpGet("{id:int}"), Authorize]
    public async Task<IActionResult> GetById(int id)
    {
        if (!await HasPermAsync("antennas.view")) return Forbidden("antennas.view");
        var licId = GetLicenseId();
        var entity = await _db.antennas.Include(a => a.Reader)
            .FirstOrDefaultAsync(a => a.id == id && a.Reader != null && a.Reader.LicenseId == licId);
        if (entity == null) return NotFound();
        return Ok(await MapToDtoAsync(entity));
    }

    [HttpGet("by-reader/{readerId:int}"), Authorize]
    public async Task<IActionResult> ByReader(int readerId)
    {
        if (!await HasPermAsync("antennas.view")) return Forbidden("antennas.view");
        var licId = GetLicenseId();
        // Verify reader belongs to this tenant
        var readerExists = await _db.readers.AnyAsync(r => r.id == readerId && r.LicenseId == licId);
        if (!readerExists) return NotFound();
        var list = await _db.antennas.Where(a => a.ReaderId == readerId).OrderBy(a => a.Port).ToListAsync();
        var result = new List<AntennaDto>(list.Count);
        foreach (var a in list) result.Add(await MapToDtoAsync(a));
        return Ok(result);
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Add([FromBody] AntennaCreateDto dto)
    {
        if (!await HasPermAsync("antennas.manage")) return Forbidden("antennas.manage");
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var licId = GetLicenseId();
        // Ensure the reader belongs to this tenant
        var readerOwned = await _db.readers.AnyAsync(r => r.id == dto.ReaderId && r.LicenseId == licId);
        if (!readerOwned) return BadRequest($"Reader {dto.ReaderId} ne obstaja ali ne pripada vaši licenci.");
        if (await _db.antennas.AnyAsync(a => a.ReaderId == dto.ReaderId && a.Port == dto.Port))
            return BadRequest($"Reader {dto.ReaderId} že ima anteno na portu {dto.Port}.");

        var entity = new Antenna { ReaderId = dto.ReaderId, Port = dto.Port, ZoneId = dto.ZoneId, RoleID = dto.RoleID };
        _db.antennas.Add(entity);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = entity.id }, await MapToDtoAsync(entity));
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, [FromBody] AntennaCreateDto dto)
    {
        if (!await HasPermAsync("antennas.manage")) return Forbidden("antennas.manage");
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var licId = GetLicenseId();
        var entity = await _db.antennas.Include(a => a.Reader)
            .FirstOrDefaultAsync(a => a.id == id && a.Reader != null && a.Reader.LicenseId == licId);
        if (entity == null) return NotFound();
        if (await _db.antennas.AnyAsync(a => a.ReaderId == dto.ReaderId && a.Port == dto.Port && a.id != id))
            return BadRequest($"Reader {dto.ReaderId} že ima anteno na portu {dto.Port}.");

        entity.ReaderId = dto.ReaderId; entity.Port = dto.Port;
        entity.ZoneId   = dto.ZoneId;   entity.RoleID = dto.RoleID;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasPermAsync("antennas.manage")) return Forbidden("antennas.manage");
        var licId = GetLicenseId();
        var entity = await _db.antennas.Include(a => a.Reader)
            .FirstOrDefaultAsync(a => a.id == id && a.Reader != null && a.Reader.LicenseId == licId);
        if (entity == null) return NotFound();
        _db.antennas.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
