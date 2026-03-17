using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Contracts;
using Signalko.Web.Services;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : PermissionedController
{
    public UserController(AppDbContext db) : base(db) {}

    // ── GET /api/User  ────────────────────────────────────────────────────────
    [HttpGet, Authorize]
    public async Task<IActionResult> GetUsers()
    {
        if (!await HasPermAsync("users.view")) return Forbidden("users.view");
        var licId = GetLicenseId();
        var users = await _db.users
            .Include(u => u.Role)
            .Where(u => u.LicenseId == licId)
            .AsNoTracking()
            .ToListAsync();

        return Ok(users.Select(u => new UserAdminDto(
            u.id, u.CardID, u.Name, u.Surname, u.Email, u.RoleId, u.Role?.Name, u.CardEpc, u.IsActive, u.Language
        )));
    }

    // ── GET /api/User/roles ───────────────────────────────────────────────────
    [HttpGet("roles"), Authorize]
    public async Task<IActionResult> GetRoles()
    {
        var licId = GetLicenseId();
        return Ok(await _db.Roles.AsNoTracking()
            .Where(r => r.LicenseId == null || r.LicenseId == licId)
            .ToListAsync());
    }

    // ── GET /api/User/{id} ────────────────────────────────────────────────────
    // Own profile always allowed; viewing others requires users.view
    [HttpGet("{id:int}"), Authorize]
    public async Task<IActionResult> GetUser(int id)
    {
        if (GetUserId() != id && !await HasPermAsync("users.view")) return Forbidden("users.view");
        var licId = GetLicenseId();
        var u = await _db.users
            .Include(x => x.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.id == id && x.LicenseId == licId);
        if (u == null) return NotFound();
        return Ok(new UserAdminDto(u.id, u.CardID, u.Name, u.Surname, u.Email, u.RoleId, u.Role?.Name, u.CardEpc, u.IsActive, u.Language));
    }

    // ── GET /api/User/by-card/{cardId} ────────────────────────────────────────
    // No auth — used by hardware/RFID scanners
    [HttpGet("by-card/{cardId}")]
    public async Task<IActionResult> GetByCard(string cardId)
    {
        var u = await _db.users
            .Include(x => x.Role)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CardID == cardId);
        if (u == null) return NotFound();
        return Ok(new UserAdminDto(u.id, u.CardID, u.Name, u.Surname, u.Email, u.RoleId, u.Role?.Name, u.CardEpc, u.IsActive, u.Language));
    }

    // ── POST /api/User ────────────────────────────────────────────────────────
    [HttpPost, Authorize]
    public async Task<IActionResult> AddUser([FromBody] UserCreateDto dto)
    {
        if (!await HasPermAsync("users.manage")) return Forbidden("users.manage");
        var licId = GetLicenseId();
        if (await _db.users.AnyAsync(u => u.Email == dto.Email))
            return Conflict(new { message = "Email je že registriran." });
        if (await _db.users.AnyAsync(u => u.CardID == dto.CardID))
            return Conflict(new { message = "CardID je že zaseden." });

        // License: check active user limit for this tenant
        var lic = licId.HasValue
            ? await _db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.id == licId.Value)
            : null;
        if (lic != null)
        {
            var activeCount = await _db.users.CountAsync(u => u.IsActive && u.LicenseId == licId);
            if (activeCount >= lic.MaxUsers)
                return Conflict(new { message = $"Dosežena omejitev licence ({lic.MaxUsers} aktivnih uporabnikov). Deaktiviraj obstoječega uporabnika ali nadgradi licenco." });
        }

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
            IsActive     = true,
            LicenseId    = licId,
        };
        _db.users.Add(entity);
        await _db.SaveChangesAsync();

        await _db.Entry(entity).Reference(u => u.Role).LoadAsync();
        return Ok(new UserAdminDto(entity.id, entity.CardID, entity.Name, entity.Surname, entity.Email, entity.RoleId, entity.Role?.Name, entity.CardEpc, entity.IsActive, entity.Language));
    }

    // ── PUT /api/User/{id} ────────────────────────────────────────────────────
    [HttpPut("{id:int}"), Authorize]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UserUpdateDto dto)
    {
        var callerId = GetUserId();
        var isSelf = callerId.HasValue && callerId.Value == id;
        // Own profile is always allowed; editing others requires users.manage
        if (!isSelf && !await HasPermAsync("users.manage")) return Forbidden("users.manage");
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
        if (dto.RoleId.HasValue && dto.RoleId != entity.RoleId)
        {
            // Guard: at least one Admin must always exist
            var adminRole = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole != null && entity.RoleId == adminRole.id)
            {
                var otherAdmins = await _db.users.CountAsync(u => u.RoleId == adminRole.id && u.id != id);
                if (otherAdmins == 0)
                    return Conflict(new { message = "Vsaj en uporabnik mora imeti vlogo Admin. Najprej dodeli Admin vlogo drugemu uporabniku." });
            }
            entity.RoleId = dto.RoleId;
        }
        if (dto.CardEpc != null)                      entity.CardEpc = string.IsNullOrWhiteSpace(dto.CardEpc) ? null : dto.CardEpc.Trim();
        if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;
        if (!string.IsNullOrWhiteSpace(dto.Language)) entity.Language = dto.Language;

        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(u => u.Role).LoadAsync();

        return Ok(new UserAdminDto(entity.id, entity.CardID, entity.Name, entity.Surname, entity.Email, entity.RoleId, entity.Role?.Name, entity.CardEpc, entity.IsActive, entity.Language));
    }

    // ── PATCH /api/User/{id}/toggle ───────────────────────────────────────────
    [HttpPatch("{id:int}/toggle"), Authorize]
    public async Task<IActionResult> Toggle(int id)
    {
        if (!await HasPermAsync("users.manage")) return Forbidden("users.manage");
        var licId = GetLicenseId();
        var entity = await _db.users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.id == id && u.LicenseId == licId);
        if (entity == null) return NotFound();

        // Guard: cannot deactivate last Admin
        if (entity.IsActive)
        {
            var adminRole = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole != null && entity.RoleId == adminRole.id)
            {
                var otherActiveAdmins = await _db.users.CountAsync(u => u.RoleId == adminRole.id && u.IsActive && u.id != id && u.LicenseId == licId);
                if (otherActiveAdmins == 0)
                    return Conflict(new { message = "Vsaj en Admin mora ostati aktiven." });
            }
        }

        // Guard: check license limit when activating
        if (!entity.IsActive)
        {
            var lic = licId.HasValue
                ? await _db.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.id == licId.Value)
                : null;
            if (lic != null)
            {
                var activeCount = await _db.users.CountAsync(u => u.IsActive && u.LicenseId == licId);
                if (activeCount >= lic.MaxUsers)
                    return Conflict(new { message = $"Licenca dovoljuje največ {lic.MaxUsers} aktivnih uporabnikov." });
            }
        }

        entity.IsActive = !entity.IsActive;
        await _db.SaveChangesAsync();
        return Ok(new UserAdminDto(entity.id, entity.CardID, entity.Name, entity.Surname, entity.Email, entity.RoleId, entity.Role?.Name, entity.CardEpc, entity.IsActive, entity.Language));
    }

    // ── DELETE /api/User/{id} ─────────────────────────────────────────────────
    [HttpDelete("{id:int}"), Authorize]
    public async Task<IActionResult> DeleteUser(int id)
    {
        if (!await HasPermAsync("users.manage")) return Forbidden("users.manage");
        var entity = await _db.users.FirstOrDefaultAsync(u => u.id == id);
        if (entity == null) return NotFound();

        // Guard: at least one Admin must always exist
        var adminRole = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == "Admin");
        if (adminRole != null && entity.RoleId == adminRole.id)
        {
            var otherAdmins = await _db.users.CountAsync(u => u.RoleId == adminRole.id && u.id != id);
            if (otherAdmins == 0)
                return Conflict(new { message = "Vsaj en uporabnik mora imeti vlogo Admin. Najprej dodeli Admin vlogo drugemu uporabniku." });
        }

        _db.users.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
