using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Core;
using Signalko.Web.Contracts;

namespace Signalko.Web.Controllers;

// upravljanje anten (antennas)
[ApiController]
[Route("api/[controller]")]
public class AntennaController : ControllerBase
{
    private readonly AppDbContext _db;

    public AntennaController(AppDbContext db) => _db = db;

    // helper: prebere RoleName iz baze preko FK RoleID
    private async Task<AntennaDto> MapToDtoAsync(Antenna a)
    {
        // predpostavljamo, da imaš v AppDbContext DbSet<Role> roles,
        // kjer so tako user kot antenne role, tukaj pa nas zanima samo ime po ID-ju
        var role = await _db.Role
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.id == a.RoleID);

        var roleName = role?.Name; // lahko je null, UI to zna prikazat

        return new AntennaDto(
            Id: a.id,
            ReaderId: a.ReaderId,
            Port: a.Port,
            ZoneId: a.ZoneId,
            RoleID: a.RoleID,
            RoleName: roleName
        );
    }

    // GET /api/antenna  (opcijsko ?readerId=1)
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AntennaDto>>> GetAll([FromQuery] int? readerId)
    {
        IQueryable<Antenna> query = _db.antennas.AsNoTracking();

        if (readerId.HasValue)
            query = query.Where(a => a.ReaderId == readerId.Value);

        var list = await query
            .OrderBy(a => a.ReaderId)
            .ThenBy(a => a.Port)
            .ToListAsync();

        var result = new List<AntennaDto>(list.Count);
        foreach (var a in list)
        {
            result.Add(await MapToDtoAsync(a));
        }

        return Ok(result);
    }

    // GET /api/antenna/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AntennaDto>> GetById(int id)
    {
        var entity = await _db.antennas
            .FirstOrDefaultAsync(a => a.id == id);

        if (entity == null)
            return NotFound();

        return Ok(await MapToDtoAsync(entity));
    }

    // GET /api/antenna/by-reader/{readerId}
    [HttpGet("by-reader/{readerId:int}")]
    public async Task<ActionResult<IEnumerable<AntennaDto>>> ByReader(int readerId)
    {
        var list = await _db.antennas
            .Where(a => a.ReaderId == readerId)
            .OrderBy(a => a.Port)
            .ToListAsync();

        var result = new List<AntennaDto>(list.Count);
        foreach (var a in list)
        {
            result.Add(await MapToDtoAsync(a));
        }

        return Ok(result);
    }

    // POST /api/antenna
    [HttpPost]
    public async Task<ActionResult<AntennaDto>> Add([FromBody] AntennaCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        // PREPREČI: isti reader + isti port
        var exists = await _db.antennas.AnyAsync(a =>
            a.ReaderId == dto.ReaderId &&
            a.Port == dto.Port
        );

        if (exists)
        {
            return BadRequest($"Reader {dto.ReaderId} že ima anteno na portu {dto.Port}.");
        }

        var entity = new Antenna
        {
            ReaderId = dto.ReaderId,
            Port = dto.Port,
            ZoneId = dto.ZoneId,
            RoleID = dto.RoleID
        };

        _db.antennas.Add(entity);
        await _db.SaveChangesAsync();

        var resultDto = await MapToDtoAsync(entity);
        return CreatedAtAction(nameof(GetById), new { id = entity.id }, resultDto);
    }

    // PUT /api/antenna/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AntennaCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var entity = await _db.antennas.FirstOrDefaultAsync(a => a.id == id);
        if (entity == null)
            return NotFound();

        // PREPREČI: isti reader + isti port (razen te antene)
        var exists = await _db.antennas.AnyAsync(a =>
            a.ReaderId == dto.ReaderId &&
            a.Port == dto.Port &&
            a.id != id
        );

        if (exists)
        {
            return BadRequest($"Reader {dto.ReaderId} že ima anteno na portu {dto.Port}.");
        }

        entity.ReaderId = dto.ReaderId;
        entity.Port = dto.Port;
        entity.ZoneId = dto.ZoneId;
        entity.RoleID = dto.RoleID;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // DELETE /api/antenna/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.antennas.FirstOrDefaultAsync(a => a.id == id);
        if (entity == null)
            return NotFound();

        _db.antennas.Remove(entity);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
