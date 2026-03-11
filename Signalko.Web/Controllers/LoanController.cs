using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Contracts;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LoanController : PermissionedController
{
    private const int LOAN_ROLE_ID = 1;

    public LoanController(AppDbContext db) : base(db) {}

    [HttpGet, Authorize]
    public async Task<IActionResult> GetAll([FromQuery] bool? active)
    {
        if (!await HasPermAsync("loans.view")) return Forbidden("loans.view");
        var q = _db.assets_loans
            .Include(l => l.Asset)
            .Include(l => l.User)
            .Include(l => l.Zone)
            .AsNoTracking();

        if (active == true)
            q = q.Where(l => l.ReturnedAt == null);

        var list = await q.OrderByDescending(l => l.LoanedAt).ToListAsync();

        var result = list.Select(l => new LoanResponseDto(
            Id:          l.id,
            AssetId:     l.AssetId,
            AssetName:   l.Asset?.Name,
            UserId:      l.UserId,
            UserName:    l.User != null
                             ? $"{l.User.Name} {l.User.Surname}".Trim()
                             : null,
            ZoneId:      l.ZoneId,
            ZoneName:    l.Zone?.Name,
            LoanedAt:    l.LoanedAt,
            ReturnedAt:  l.ReturnedAt,
            Active:      l.ReturnedAt == null
        ));

        return Ok(result);
    }

    [HttpGet("my"), Authorize]
    public async Task<IActionResult> GetMine([FromQuery] int? userId)
    {
        // Try to get userId from JWT sub claim
        var subClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("id")?.Value;
        int uid = 0;
        if (!int.TryParse(subClaim, out uid) && userId.HasValue)
            uid = userId.Value;

        if (uid == 0)
            return BadRequest("Ni mogoče določiti uporabnika. Priloži JWT ali ?userId=.");

        var list = await _db.assets_loans
            .Where(l => l.UserId == uid)
            .Include(l => l.Asset)
            .Include(l => l.Zone)
            .OrderByDescending(l => l.LoanedAt)
            .AsNoTracking()
            .ToListAsync();

        var result = list.Select(l => new LoanResponseDto(
            Id:         l.id,
            AssetId:    l.AssetId,
            AssetName:  l.Asset?.Name,
            UserId:     l.UserId,
            UserName:   null,
            ZoneId:     l.ZoneId,
            ZoneName:   l.Zone?.Name,
            LoanedAt:   l.LoanedAt,
            ReturnedAt: l.ReturnedAt,
            Active:     l.ReturnedAt == null
        ));

        return Ok(result);
    }

    [HttpGet("{id:int}"), Authorize]
    public async Task<IActionResult> GetOne(int id)
    {
        var l = await _db.assets_loans
            .Include(x => x.Asset)
            .Include(x => x.User)
            .Include(x => x.Zone)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.id == id);

        if (l == null) return NotFound();

        return Ok(new LoanResponseDto(
            Id:         l.id,
            AssetId:    l.AssetId,
            AssetName:  l.Asset?.Name,
            UserId:     l.UserId,
            UserName:   l.User != null ? $"{l.User.Name} {l.User.Surname}".Trim() : null,
            ZoneId:     l.ZoneId,
            ZoneName:   l.Zone?.Name,
            LoanedAt:   l.LoanedAt,
            ReturnedAt: l.ReturnedAt,
            Active:     l.ReturnedAt == null
        ));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Create([FromBody] LoanCreateRequestDto dto)
    {
        if (!await HasPermAsync("loans.create")) return Forbidden("loans.create");
        var asset = await _db.ASSET.AsNoTracking().FirstOrDefaultAsync(a => a.id == dto.AssetId);
        if (asset == null)
            return NotFound($"Sredstvo z ID {dto.AssetId} ne obstaja.");

        // Validate user
        var user = await _db.users.AsNoTracking().FirstOrDefaultAsync(u => u.id == dto.UserId);
        if (user == null)
            return NotFound($"Uporabnik z ID {dto.UserId} ne obstaja.");

        // Check if this user already has this asset
        var myExisting = await _db.assets_loans
            .FirstOrDefaultAsync(l => l.AssetId == dto.AssetId && l.UserId == dto.UserId && l.ReturnedAt == null);
        if (myExisting != null)
            return Conflict(new { message = "Ta uporabnik že ima to sredstvo v izposoji.", loanId = myExisting.id });

        // Check if someone else has it
        var otherActive = await _db.assets_loans
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.AssetId == dto.AssetId && l.ReturnedAt == null);
        if (otherActive != null)
            return Conflict(new
            {
                message = "Sredstvo je trenutno pri drugi osebi.",
                loanId  = otherActive.id,
                userId  = otherActive.UserId,
                userName = otherActive.User != null
                    ? $"{otherActive.User.Name} {otherActive.User.Surname}".Trim()
                    : null
            });

        var loan = new AssetLoan
        {
            AssetId    = dto.AssetId,
            UserId     = dto.UserId,
            ZoneId     = dto.ZoneId,
            LoanedAt   = DateTime.UtcNow,
            ReturnedAt = null,
        };

        _db.assets_loans.Add(loan);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOne), new { id = loan.id }, new LoanResponseDto(
            Id:         loan.id,
            AssetId:    loan.AssetId,
            AssetName:  asset.Name,
            UserId:     loan.UserId,
            UserName:   $"{user.Name} {user.Surname}".Trim(),
            ZoneId:     loan.ZoneId,
            ZoneName:   null,
            LoanedAt:   loan.LoanedAt,
            ReturnedAt: null,
            Active:     true
        ));
    }

    [HttpPost("return"), Authorize]
    public async Task<IActionResult> Return([FromBody] LoanReturnDto dto)
    {
        if (!await HasPermAsync("loans.return")) return Forbidden("loans.return");
        var loan = await _db.assets_loans.FirstOrDefaultAsync(l => l.id == dto.LoanId);
        if (loan == null)
            return NotFound($"Izposoja z ID {dto.LoanId} ne obstaja.");

        if (loan.ReturnedAt != null)
            return Conflict(new { message = "Ta izposoja je že zaprta." });

        loan.ReturnedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Sredstvo vrnjeno.", loanId = loan.id, returnedAt = loan.ReturnedAt });
    }

    [HttpGet("last-users-by-antenna/{antennaId:int}"), Authorize]
    public async Task<IActionResult> LastUsersByAntenna(int antennaId)
    {
        var antenna = await _db.antennas.AsNoTracking().FirstOrDefaultAsync(a => a.id == antennaId);
        if (antenna == null) return NotFound($"Antena z ID {antennaId} ne obstaja.");
        if (antenna.RoleID != LOAN_ROLE_ID) return BadRequest("Ta antena nima role LOAN.");

        var zone = await _db.zones.AsNoTracking().FirstOrDefaultAsync(z => z.id == antenna.ZoneId);
        if (zone == null) return BadRequest("Antena nima nastavljene cone.");

        var query =
            from t in _db.TAG
            where t.Antenna == antennaId
            join u in _db.users on t.Epc equals u.CardID
            group new { t, u } by new { u.id, u.Name, u.Surname, u.CardID } into g
            let last = g.OrderByDescending(x => x.t.Time).FirstOrDefault()
            where last != null
            select new LoanUserLastSeenDto(
                g.Key.id,
                (g.Key.Name ?? "") + " " + (g.Key.Surname ?? ""),
                g.Key.CardID ?? "",
                last!.t.Time ?? DateTime.MinValue,
                antennaId,
                "Antena " + antennaId,
                zone.id,
                zone.Name ?? ""
            );

        var list = await query.OrderByDescending(x => x.LastSeen).ToListAsync();
        return Ok(list);
    }
}
