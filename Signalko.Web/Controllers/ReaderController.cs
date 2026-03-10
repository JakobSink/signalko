using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Core;
using Signalko.Core.DTOs;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReaderController : ControllerBase
{
    private readonly AppDbContext _db;
    public ReaderController(AppDbContext db) => _db = db;

    // 🇸🇮 VRNE vse readerje
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var list = await _db.readers
            .AsNoTracking()
            .Include(r => r.Antennas)
            .Select(r => new ReaderDto
            {
                Id = r.id,
                Name = r.Name,
                Ip = r.IP,
                Hostname = r.Hostname,
                Enabled = r.Enabled
            })
            .ToListAsync();

        return Ok(list);
    }

    // 🇸🇮 DODA novega readerja
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] ReaderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Ime je obvezno.");
        if (string.IsNullOrWhiteSpace(dto.Ip))
            return BadRequest("IP je obvezen.");

        var entity = new Reader
        {
            Name = dto.Name.Trim(),
            IP = dto.Ip.Trim(),
            Hostname = string.IsNullOrWhiteSpace(dto.Hostname) ? null : dto.Hostname.Trim(),
            Enabled = dto.Enabled
        };

        _db.readers.Add(entity);
        await _db.SaveChangesAsync();

        dto.Id = entity.id;
        return Ok(dto);
    }

    // 🇸🇮 POSODOBI obstoječega readerja
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ReaderDto dto)
    {
        var entity = await _db.readers.FindAsync(id);
        if (entity == null)
            return NotFound($"Reader z ID={id} ne obstaja.");

        // 🇸🇮 Posodobitev lastnosti
        if (!string.IsNullOrWhiteSpace(dto.Name))
            entity.Name = dto.Name.Trim();

        if (!string.IsNullOrWhiteSpace(dto.Ip))
            entity.IP = dto.Ip.Trim();

        entity.Hostname = string.IsNullOrWhiteSpace(dto.Hostname) ? null : dto.Hostname.Trim();
        entity.Enabled = dto.Enabled;

        await _db.SaveChangesAsync(); // 🇸🇮 to generira SQL UPDATE

        return Ok(new { ok = true });
    }

    // 🇸🇮 IZBRIŠE readerja
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.readers.FindAsync(id);
        if (entity == null)
            return NotFound();

        _db.readers.Remove(entity);
        await _db.SaveChangesAsync();

        return Ok(new { ok = true });
    }
}
