using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/AntennaRole")]
public class AntennaRoleController : ControllerBase
{
    private readonly AppDbContext _db;
    public AntennaRoleController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var roles = await _db.Role.AsNoTracking().ToListAsync();
        return Ok(roles);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AntennaRoleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.name)) return BadRequest(new { message = "Ime je obvezno." });
        var role = new AntennaRole { Name = dto.name.Trim() };
        _db.Role.Add(role);
        await _db.SaveChangesAsync();
        return Ok(role);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var role = await _db.Role.FirstOrDefaultAsync(r => r.id == id);
        if (role == null) return NotFound();
        _db.Role.Remove(role);
        await _db.SaveChangesAsync();
        return Ok(new { message = "Vloga izbrisana." });
    }
}

public record AntennaRoleDto(string? name);
