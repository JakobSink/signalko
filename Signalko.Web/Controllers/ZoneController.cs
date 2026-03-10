using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Core;

namespace Signalko.Web.Controllers;

// DTO-ji za cone in antene – damo v isti file, da je vse na enem mestu
public record ZoneDto(int id, string? Name, string? Type);

public record ZoneAntennaDto(
    int AntennaId,
    int Port,
    int ReaderId,
    string? ReaderName,
    string? ReaderIP,
    bool ReaderEnabled,
    int RoleId,
    string? RoleName
);

public record ZoneWithAntennasDto(
    int id,
    string? Name,
    string? Type,
    List<ZoneAntennaDto> Antennas
);

public record AssignAntennaZoneDto(
    int AntennaId,
    int ZoneId   // 0 = "ni dodeljeno"
);

// upravljanje con (zones)
[ApiController]
[Route("api/[controller]")]
public class ZoneController : ControllerBase
{
    private readonly AppDbContext _db;
    public ZoneController(AppDbContext db) => _db = db;

    // ======== OBSTOJEČI ENDPOINTI (pustimo, da ne podremo ničesar) ========

    // GET: /api/zone  -> vrne vse cone (entitete)
    [HttpGet]
    public async Task<IActionResult> GetZones()
        => Ok(await _db.zones.ToListAsync());

    // POST: /api/zone  -> doda cono
    [HttpPost]
    public async Task<IActionResult> AddZone([FromBody] Zone zone)
    {
        _db.zones.Add(zone);
        await _db.SaveChangesAsync();
        return Ok(zone);
    }

    // ======== DODATNI CRUD (opcijsko, če boš rabil) ========

    // GET: /api/zone/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<ZoneDto>> GetZone(int id)
    {
        var z = await _db.zones.FindAsync(id);
        if (z is null) return NotFound();

        return new ZoneDto(z.id, z.Name, z.Type);
    }

    // PUT: /api/zone/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateZone(int id, [FromBody] ZoneDto dto)
    {
        var z = await _db.zones.FindAsync(id);
        if (z is null) return NotFound(new { message = $"Cona #{id} ne obstaja." });

        z.Name = dto.Name;
        z.Type = dto.Type;
        await _db.SaveChangesAsync();
        return Ok(new ZoneDto(z.id, z.Name, z.Type));
    }

    // DELETE: /api/zone/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteZone(int id)
    {
        var z = await _db.zones
            .Include(x => x.Antennas)
            .FirstOrDefaultAsync(x => x.id == id);

        if (z is null) return NotFound();

        if (z.Antennas.Any())
            return BadRequest(new { message = "Cona ima dodeljene antene. Najprej jih prestavi drugam." });

        _db.zones.Remove(z);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ======== ZONE + ANTENNE ZA NOV UI ========

    // GET: /api/zone/with-antennas
    // Vrne kartice: vsaka cona + seznam anten, ki imajo ZoneId = id te cone
    [HttpGet("with-antennas")]
    public async Task<IEnumerable<ZoneWithAntennasDto>> GetWithAntennas()
    {
        var zones = await _db.zones
            .OrderBy(z => z.Name)
            .ToListAsync();

        var antennas = await _db.antennas
            .Include(a => a.Reader)
            .Include(a => a.Role)
            .ToListAsync();

        var result = new List<ZoneWithAntennasDto>();

        // normalne cone
        foreach (var z in zones)
        {
            var ants = antennas
                .Where(a => a.ZoneId == z.id)
                .OrderBy(a => a.Reader!.Name)
                .ThenBy(a => a.Port)
                .Select(a => new ZoneAntennaDto(
                    a.id,
                    a.Port,
                    a.ReaderId,
                    a.Reader?.Name,
                    a.Reader?.IP,
                    a.Reader?.Enabled ?? false,
                    a.RoleID,
                    a.Role?.Name
                ))
                .ToList();

            result.Add(new ZoneWithAntennasDto(z.id, z.Name, z.Type, ants));
        }

        // posebna "cona" za antene brez cone (ZoneId = 0)
        var unassigned = antennas
            .Where(a => a.ZoneId == 0)
            .OrderBy(a => a.Reader!.Name)
            .ThenBy(a => a.Port)
            .Select(a => new ZoneAntennaDto(
                a.id,
                a.Port,
                a.ReaderId,
                a.Reader?.Name,
                a.Reader?.IP,
                a.Reader?.Enabled ?? false,
                a.RoleID,
                a.Role?.Name
            ))
            .ToList();

        if (unassigned.Count > 0)
        {
            result.Insert(0, new ZoneWithAntennasDto(
                0,
                "Ni dodeljeno",
                null,
                unassigned
            ));
        }

        return result;
    }

    // POST: /api/zone/assign-antenna
    // Premakne eno anteno v izbrano cono (ali 0 = ni dodeljeno)
    [HttpPost("assign-antenna")]
    public async Task<IActionResult> AssignAntenna([FromBody] AssignAntennaZoneDto dto)
    {
        var ant = await _db.antennas.FindAsync(dto.AntennaId);
        if (ant is null) return NotFound("Antenna not found.");

        if (dto.ZoneId != 0)
        {
            var exists = await _db.zones.AnyAsync(z => z.id == dto.ZoneId);
            if (!exists) return BadRequest("Zone does not exist.");
        }

        ant.ZoneId = dto.ZoneId;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
