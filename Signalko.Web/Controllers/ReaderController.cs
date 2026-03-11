using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Core;
using Signalko.Core.DTOs;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReaderController : PermissionedController
{
    public ReaderController(AppDbContext db) : base(db) {}

    [HttpGet, Authorize]
    public async Task<IActionResult> Get()
    {
        if (!await HasPermAsync("readers.view")) return Forbidden("readers.view");
        var list = await _db.readers
            .AsNoTracking()
            .Include(r => r.Antennas)
            .Select(r => new ReaderDto
            {
                Id = r.id, Name = r.Name, Ip = r.IP,
                Hostname = r.Hostname, Enabled = r.Enabled
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Add([FromBody] ReaderDto dto)
    {
        if (!await HasPermAsync("readers.manage")) return Forbidden("readers.manage");
        if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest("Ime je obvezno.");
        if (string.IsNullOrWhiteSpace(dto.Ip))   return BadRequest("IP je obvezen.");

        var entity = new Reader
        {
            Name     = dto.Name.Trim(),
            IP       = dto.Ip.Trim(),
            Hostname = string.IsNullOrWhiteSpace(dto.Hostname) ? null : dto.Hostname.Trim(),
            Enabled  = dto.Enabled
        };
        _db.readers.Add(entity);
        await _db.SaveChangesAsync();
        dto.Id = entity.id;
        return Ok(dto);
    }

    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> Update(int id, [FromBody] ReaderDto dto)
    {
        if (!await HasPermAsync("readers.manage")) return Forbidden("readers.manage");
        var entity = await _db.readers.FindAsync(id);
        if (entity == null) return NotFound($"Reader z ID={id} ne obstaja.");

        if (!string.IsNullOrWhiteSpace(dto.Name)) entity.Name = dto.Name.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Ip))   entity.IP   = dto.Ip.Trim();
        entity.Hostname = string.IsNullOrWhiteSpace(dto.Hostname) ? null : dto.Hostname.Trim();
        entity.Enabled  = dto.Enabled;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await HasPermAsync("readers.manage")) return Forbidden("readers.manage");
        var entity = await _db.readers.FindAsync(id);
        if (entity == null) return NotFound();
        _db.readers.Remove(entity);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}
