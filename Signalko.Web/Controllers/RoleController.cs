using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Core;
using Signalko.Web.Contracts;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoleController : ControllerBase
{
    private readonly AppDbContext _db;

    public RoleController(AppDbContext db)
    {
        _db = db;
    }

    // GET /api/Role
    // Vrne vse ANTENNA role (AntennaRole iz tabele "Role")
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetAll()
    {
        var list = await _db.Role
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(
                r.id,          // DTO: int id
                r.Name!        // DTO: string Name
            ))
            .ToListAsync();

        return Ok(list);
    }

    // POST /api/Role
    // Ustvari NA NOVO role za antene
    [HttpPost]
    public async Task<ActionResult<RoleDto>> Create([FromBody] RoleCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest("Name je obvezen.");

        var entity = new AntennaRole
        {
            Name = dto.Name.Trim()
        };

        _db.Role.Add(entity);
        await _db.SaveChangesAsync();

        var result = new RoleDto(entity.id, entity.Name!);
        return CreatedAtAction(nameof(GetById), new { id = entity.id }, result);
    }

    // GET /api/Role/{id}
    [HttpGet("{id:int}")]
    public async Task<ActionResult<RoleDto>> GetById(int id)
    {
        var entity = await _db.Role
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.id == id);

        if (entity == null)
            return NotFound();

        return new RoleDto(entity.id, entity.Name!);
    }

    // DELETE /api/Role/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var entity = await _db.Role.FirstOrDefaultAsync(r => r.id == id);
        if (entity == null)
            return NotFound();

        _db.Role.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
