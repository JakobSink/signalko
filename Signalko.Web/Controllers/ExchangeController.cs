using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Contracts;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExchangeController : ControllerBase
{
    private readonly AppDbContext _db;
    public ExchangeController(AppDbContext db) => _db = db;

    // ── GET /api/Exchange/pending?toUserId={id} ───────────────────────────────
    // Returns pending exchange requests directed at a user
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] int toUserId)
    {
        if (toUserId <= 0) return BadRequest("toUserId je obvezen.");

        var list = await _db.ExchangeRequests
            .Where(e => e.ToUserId == toUserId && e.Status == "pending")
            .Include(e => e.FromUser)
            .Include(e => e.ToUser)
            .Include(e => e.Asset)
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return Ok(list.Select(MapDto));
    }

    // ── GET /api/Exchange/my?userId={id} ─────────────────────────────────────
    // Returns all exchange requests sent BY a user
    [HttpGet("my")]
    public async Task<IActionResult> GetMine([FromQuery] int userId)
    {
        if (userId <= 0) return BadRequest("userId je obvezen.");

        var list = await _db.ExchangeRequests
            .Where(e => e.FromUserId == userId)
            .Include(e => e.FromUser)
            .Include(e => e.ToUser)
            .Include(e => e.Asset)
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return Ok(list.Select(MapDto));
    }

    // ── GET /api/Exchange/all ─────────────────────────────────────────────────
    [HttpGet("all")]
    public async Task<IActionResult> GetAll()
    {
        var list = await _db.ExchangeRequests
            .Include(e => e.FromUser)
            .Include(e => e.ToUser)
            .Include(e => e.Asset)
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();
        return Ok(list.Select(MapDto));
    }

    // ── POST /api/Exchange ────────────────────────────────────────────────────
    // Create a new exchange request
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExchangeCreateDto dto)
    {
        if (dto.FromUserId <= 0)
            return BadRequest(new { message = "Uporabnik ni identificiran. Skeniraj najprej kartico." });
        if (!await _db.users.AnyAsync(u => u.id == dto.FromUserId))
            return NotFound(new { message = $"Uporabnik #{dto.FromUserId} ne obstaja." });
        if (!await _db.users.AnyAsync(u => u.id == dto.ToUserId))
            return NotFound(new { message = $"Imetnik #{dto.ToUserId} ne obstaja." });
        if (!await _db.ASSET.AnyAsync(a => a.id == dto.AssetId))
            return NotFound(new { message = $"Sredstvo #{dto.AssetId} ne obstaja." });

        // Cancel any existing pending request for the same combo
        var existing = await _db.ExchangeRequests
            .FirstOrDefaultAsync(e => e.FromUserId == dto.FromUserId
                                   && e.AssetId    == dto.AssetId
                                   && e.Status     == "pending");
        if (existing != null) { existing.Status = "cancelled"; }

        var req = new ExchangeRequest
        {
            FromUserId  = dto.FromUserId,
            ToUserId    = dto.ToUserId,
            AssetId     = dto.AssetId,
            Message     = dto.Message,
            Status      = "pending",
            CreatedAt   = DateTime.UtcNow,
        };

        _db.ExchangeRequests.Add(req);
        await _db.SaveChangesAsync();

        await _db.Entry(req).Reference(e => e.FromUser).LoadAsync();
        await _db.Entry(req).Reference(e => e.ToUser).LoadAsync();
        await _db.Entry(req).Reference(e => e.Asset).LoadAsync();

        return CreatedAtAction(nameof(GetPending), new { toUserId = dto.ToUserId }, MapDto(req));
    }

    // ── PUT /api/Exchange/{id}/respond ────────────────────────────────────────
    // Accept or reject an exchange request
    [HttpPut("{id:int}/respond")]
    public async Task<IActionResult> Respond(int id, [FromBody] ExchangeRespondDto dto)
    {
        var req = await _db.ExchangeRequests.FirstOrDefaultAsync(e => e.id == id);
        if (req == null) return NotFound();
        if (req.Status != "pending")
            return Conflict(new { message = "Zahteva je že bila obdelana." });

        req.Status      = dto.Accept ? "accepted" : "rejected";
        req.RespondedAt = DateTime.UtcNow;

        if (dto.Accept)
        {
            // Return asset from current holder — new loan is created by the frontend confirm step
            var activeLoan = await _db.assets_loans
                .FirstOrDefaultAsync(l => l.AssetId == req.AssetId && l.UserId == req.ToUserId && l.ReturnedAt == null);
            if (activeLoan != null)
            {
                activeLoan.ReturnedAt = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { req.Status, req.RespondedAt });
    }

    // ── Mapper ────────────────────────────────────────────────────────────────
    private static ExchangeResponseDto MapDto(ExchangeRequest e) => new(
        e.id,
        e.FromUserId,
        e.FromUser != null ? $"{e.FromUser.Name} {e.FromUser.Surname}".Trim() : null,
        e.FromUser?.CardID,
        e.ToUserId,
        e.ToUser   != null ? $"{e.ToUser.Name} {e.ToUser.Surname}".Trim()     : null,
        e.AssetId,
        e.Asset?.Name,
        e.Status,
        e.CreatedAt,
        e.RespondedAt,
        e.Message
    );
}
