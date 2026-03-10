using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Contracts;
using Signalko.Web.Services;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly AppDbContext _db;
    public UserController(AppDbContext db) => _db = db;

    // ── GET /api/User  ────────────────────────────────────────────────────────
    // Returns all users with role info (admin use)
    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.users
            .Include(u => u.Role)
            .AsNoTracking()
            .ToListAsync();

        return Ok(users.Select(u => new UserAdminDto(
            u.id, u.CardID, u.Name, u.Surname, u.Email, u.RoleId, u.Role?.Name, u.CardEpc
        )));
    }

    // ── GET /api/User/roles ───────────────────────────────────────────────────
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
        => Ok(await _db.Roles.AsNoTracking().ToListAsync());

    // ── GET /api/User/{id} ────────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var u = await _db.users
            .Include(x => x.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.id == id);
        if (u == null) return NotFound();
        return Ok(new UserAdminDto(u.id, u.CardID, u.Name, u.Surname, u.Email, u.RoleId, u.Role?.Name, u.CardEpc));
    }

    // ── GET /api/User/by-card/{cardId} ────────────────────────────────────────
    [HttpGet("by-card/{cardId}")]
    public async Task<IActionResult> GetByCard(string cardId)
    {
        var u = await _db.users
            .Include(x => x.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CardID == cardId);
        if (u == null) return NotFound();
        return Ok(new UserAdminDto(u.id, u.CardID, u.Name, u.Surname, u.Email, u.RoleId, u.Role?.Name, u.CardEpc));
    }

    // ── POST /api/User ────────────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> AddUser([FromBody] UserCreateDto dto)
    {
        if (await _db.users.AnyAsync(u => u.Email == dto.Email))
            return Conflict(new { message = "Email je že registriran." });
        if (await _db.users.AnyAsync(u => u.CardID == dto.CardID))
            return Conflict(new { message = "CardID je že zaseden." });

        var entity = new User
        {
            CardID       = dto.CardID,
            Name         = dto.Name,
            Surname      = dto.Surname,
            Email        = dto.Email ?? "",
            ValidationId = dto.ValidationId,
            RoleId       = dto.RoleId,
            Password     = PasswordHasher.Hash(dto.Password),
            CardEpc      = string.IsNullOrWhiteSpace(dto.CardEpc) ? null : dto.CardEpc.Trim(),
        };
        _db.users.Add(entity);
        await _db.SaveChangesAsync();

        await _db.Entry(entity).Reference(u => u.Role).LoadAsync();
        return Ok(new UserAdminDto(entity.id, entity.CardID, entity.Name, entity.Surname, entity.Email, entity.RoleId, entity.Role?.Name, entity.CardEpc));
    }

    // ── PUT /api/User/{id} ────────────────────────────────────────────────────
    // Admin: update any user field except id
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateDto dto)
    {
        var entity = await _db.users.Include(u => u.Role).FirstOrDefaultAsync(u => u.id == id);
        if (entity == null) return NotFound();

        // Check uniqueness if changing CardID or Email
        if (!string.IsNullOrWhiteSpace(dto.CardID) && dto.CardID != entity.CardID)
        {
            if (await _db.users.AnyAsync(u => u.CardID == dto.CardID && u.id != id))
                return Conflict(new { message = "CardID je že zaseden." });
            entity.CardID = dto.CardID;
        }
        if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != entity.Email)
        {
            if (await _db.users.AnyAsync(u => u.Email == dto.Email && u.id != id))
                return Conflict(new { message = "Email je že registriran." });
            entity.Email = dto.Email.Trim().ToLowerInvariant();
        }
        if (!string.IsNullOrWhiteSpace(dto.Name))    entity.Name    = dto.Name;
        if (dto.Surname != null)                      entity.Surname = dto.Surname;
        if (!string.IsNullOrWhiteSpace(dto.Password)) entity.Password = PasswordHasher.Hash(dto.Password);
        if (dto.RoleId.HasValue)                      entity.RoleId  = dto.RoleId;
        if (dto.CardEpc != null)                      entity.CardEpc = string.IsNullOrWhiteSpace(dto.CardEpc) ? null : dto.CardEpc.Trim();

        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(u => u.Role).LoadAsync();

        return Ok(new UserAdminDto(entity.id, entity.CardID, entity.Name, entity.Surname, entity.Email, entity.RoleId, entity.Role?.Name, entity.CardEpc));
    }

    // ── DELETE /api/User/{id} ─────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var entity = await _db.users.FirstOrDefaultAsync(u => u.id == id);
        if (entity == null) return NotFound();
        _db.users.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
