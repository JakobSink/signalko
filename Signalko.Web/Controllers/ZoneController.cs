using Microsoft.AspNetCore.Authorization;
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
public class ZoneController : PermissionedController
{
    public ZoneController(AppDbContext db) : base(db) {}

    [HttpGet]
    public async Task<IActionResult> GetZones()
    {
        var licId = GetLicenseId();
        return Ok(await _db.zones.Where(z => z.LicenseId == licId).ToListAsync());
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> AddZone([FromBody] Zone zone)
    {
        if (!await HasPermAsync("zones.manage")) return Forbidden("zones.manage");
        zone.LicenseId = GetLicenseId();
        _db.zones.Add(zone);
        await _db.SaveChangesAsync();
        return Ok(zone);
    }

    [HttpGet("{id:int}"), Authorize]
    public async Task<IActionResult> GetZone(int id)
    {
        if (!await HasPermAsync("zones.view")) return Forbidden("zones.view");
        var licId = GetLicenseId();
        var z = await _db.zones.FirstOrDefaultAsync(x => x.id == id && x.LicenseId == licId);
        if (z is null) return NotFound();
        return Ok(new ZoneDto(z.id, z.Name, z.Type));
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> UpdateZone(int id, [FromBody] ZoneDto dto)
    {
        if (!await HasPermAsync("zones.manage")) return Forbidden("zones.manage");
        var licId = GetLicenseId();
        var z = await _db.zones.FirstOrDefaultAsync(x => x.id == id && x.LicenseId == licId);
        if (z is null) return NotFound(new { message = $"Cona #{id} ne obstaja." });

        z.Name = dto.Name;
        z.Type = dto.Type;
        await _db.SaveChangesAsync();
        return Ok(new ZoneDto(z.id, z.Name, z.Type));
    }

    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> DeleteZone(int id)
    {
        if (!await HasPermAsync("zones.manage")) return Forbidden("zones.manage");
        var licId = GetLicenseId();
        var z = await _db.zones
            .Include(x => x.Antennas)
            .FirstOrDefaultAsync(x => x.id == id && x.LicenseId == licId);

        if (z is null) return NotFound();

        if (z.Antennas.Any())
            return BadRequest(new { message = "Cona ima dodeljene antene. Najprej jih prestavi drugam." });

        _db.zones.Remove(z);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("with-antennas"), Authorize]
    public async Task<IEnumerable<ZoneWithAntennasDto>> GetWithAntennas()
    {
        if (!await HasPermAsync("zones.view")) { Response.StatusCode = 403; return Enumerable.Empty<ZoneWithAntennasDto>(); }
        var licId = GetLicenseId();
        var zones = await _db.zones.Where(z => z.LicenseId == licId).OrderBy(z => z.Name).ToListAsync();
        var antennas = await _db.antennas.Include(a => a.Reader).Include(a => a.Role)
            .Where(a => a.Reader != null && a.Reader.LicenseId == licId).ToListAsync();
        var result = new List<ZoneWithAntennasDto>();
        foreach (var z in zones)
        {
            var ants = antennas.Where(a => a.ZoneId == z.id).OrderBy(a => a.Reader!.Name).ThenBy(a => a.Port)
                .Select(a => new ZoneAntennaDto(a.id, a.Port, a.ReaderId, a.Reader?.Name, a.Reader?.IP, a.Reader?.Enabled ?? false, a.RoleID, a.Role?.Name)).ToList();
            result.Add(new ZoneWithAntennasDto(z.id, z.Name, z.Type, ants));
        }
        var unassigned = antennas.Where(a => a.ZoneId == 0).OrderBy(a => a.Reader!.Name).ThenBy(a => a.Port)
            .Select(a => new ZoneAntennaDto(a.id, a.Port, a.ReaderId, a.Reader?.Name, a.Reader?.IP, a.Reader?.Enabled ?? false, a.RoleID, a.Role?.Name)).ToList();
        if (unassigned.Count > 0) result.Insert(0, new ZoneWithAntennasDto(0, "Ni dodeljeno", null, unassigned));
        return result;
    }

    [HttpPost("assign-antenna"), Authorize]
    public async Task<IActionResult> AssignAntenna([FromBody] AssignAntennaZoneDto dto)
    {
        if (!await HasPermAsync("zones.manage")) return Forbidden("zones.manage");
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
