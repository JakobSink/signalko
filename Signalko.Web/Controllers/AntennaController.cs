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
        IQueryable<Antenna> q = _db.antennas.AsNoTracking();
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
        var entity = await _db.antennas.FirstOrDefaultAsync(a => a.id == id);
        if (entity == null) return NotFound();
        return Ok(await MapToDtoAsync(entity));
    }

    [HttpGet("by-reader/{readerId:int}"), Authorize]
    public async Task<IActionResult> ByReader(int readerId)
    {
        if (!await HasPermAsync("antennas.view")) return Forbidden("antennas.view");
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
        var entity = await _db.antennas.FirstOrDefaultAsync(a => a.id == id);
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
        var entity = await _db.antennas.FirstOrDefaultAsync(a => a.id == id);
        if (entity == null) return NotFound();
        _db.antennas.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
