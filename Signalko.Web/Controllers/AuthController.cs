using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.DTOs;
using Signalko.Web.Services;
using System.Security.Cryptography;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext   _db;
    private readonly JwtTokenService _jwt;

    public AuthController(AppDbContext db, JwtTokenService jwt)
    {
        _db  = db;
        _jwt = jwt;
    }

    // POST /api/Auth/signup
    [HttpPost("signup")]
    public async Task<IActionResult> Signup([FromBody] SignupRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Email je obvezen." });

        if (await _db.users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "Email je že registriran." });

        // First user to register gets Admin; all others get User
        var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
        var userRole  = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "User");
        var hasAdmin  = adminRole != null && await _db.users.AnyAsync(u => u.RoleId == adminRole.id);
        var assignedRole = (!hasAdmin && adminRole != null) ? adminRole : userRole;

        var user = new User
        {
            Name     = (req.Name ?? "").Trim(),
            Surname  = string.IsNullOrWhiteSpace(req.Surname) ? null : req.Surname.Trim(),
            Email    = email,
            Password = PasswordHasher.Hash(req.Password),
            CardID   = await GenerateUniqueCardIdAsync(),
            RoleId   = assignedRole?.id
        };

        _db.users.Add(user);
        await _db.SaveChangesAsync();

        var token = _jwt.CreateToken(user, assignedRole?.Name);

        return Ok(new AuthResponse
        {
            token   = token,
            id      = user.id,
            cardID  = user.CardID,
            name    = user.Name,
            surname = user.Surname,
            email   = user.Email,
            roleId  = assignedRole?.id,
            role    = assignedRole?.Name
        });
    }

    // POST /api/Auth/login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Email je obvezen." });

        var user = await _db.users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || !PasswordHasher.Verify(req.Password, user.Password))
            return Unauthorized(new { message = "Napačen email ali geslo." });

        var token = _jwt.CreateToken(user, user.Role?.Name);

        return Ok(new AuthResponse
        {
            token   = token,
            id      = user.id,
            cardID  = user.CardID,
            name    = user.Name,
            surname = user.Surname,
            email   = user.Email,
            roleId  = user.RoleId,
            role    = user.Role?.Name
        });
    }

    // ── 6-digit CardID generator ──────────────────────────────────────────────
    private async Task<string> GenerateUniqueCardIdAsync()
    {
        for (var i = 0; i < 30; i++)
        {
            var n    = RandomNumberGenerator.GetInt32(0, 1_000_000);
            var card = n.ToString("D6");
            if (!await _db.users.AnyAsync(u => u.CardID == card))
                return card;
        }
        throw new Exception("Ne morem generirati unikatnega CardID.");
    }
}
