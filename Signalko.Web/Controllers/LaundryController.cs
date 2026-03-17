using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/Laundry")]
[Authorize]
public class LaundryController : PermissionedController
{
    public LaundryController(AppDbContext db) : base(db) { }

    // ══════════════════════════════════════════════════════════════════════
    // ITEMS
    // ══════════════════════════════════════════════════════════════════════

    // GET /api/Laundry/items
    [HttpGet("items")]
    public async Task<IActionResult> GetItems()
    {
        if (!await HasPermAsync("laundry.view")) return Forbidden("laundry.view");
        var licId = GetLicenseId();

        var items = await _db.LaundryItems
            .AsNoTracking()
            .Where(i => i.LicenseId == licId)
            .Include(i => i.Owner)
            .Include(i => i.Tag)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return Ok(items.Select(i => MapItem(i)));
    }

    // GET /api/Laundry/items/by-epc/{epc}
    [HttpGet("items/by-epc/{epc}")]
    public async Task<IActionResult> GetItemByEpc(string epc)
    {
        if (!await HasPermAsync("laundry.view")) return Forbidden("laundry.view");
        var licId = GetLicenseId();

        var tag = await _db.TAG.AsNoTracking()
            .FirstOrDefaultAsync(t => (t.Epc == epc || t.EpcAscii == epc) && t.LicenseId == licId);
        if (tag == null) return NotFound(new { message = "Tag ni najden." });

        var item = await _db.LaundryItems.AsNoTracking()
            .Include(i => i.Owner)
            .Include(i => i.Tag)
            .FirstOrDefaultAsync(i => i.TagId == tag.id && i.LicenseId == licId);
        if (item == null) return NotFound(new { message = "Artikel pralnice ni registriran za ta tag." });

        return Ok(MapItem(item));
    }

    // POST /api/Laundry/items
    [HttpPost("items")]
    public async Task<IActionResult> CreateItem([FromBody] LaundryItemCreateDto dto)
    {
        if (!await HasPermAsync("laundry.manage")) return Forbidden("laundry.manage");
        var licId = GetLicenseId();
        if (!licId.HasValue) return Unauthorized();

        var item = new LaundryItem
        {
            LicenseId = licId.Value,
            OwnerId   = dto.OwnerId,
            Name      = dto.Name,
            Category  = dto.Category,
            TagId     = dto.TagId,
            Status    = "active",
            Notes     = dto.Notes,
            CreatedAt = DateTime.UtcNow,
        };
        _db.LaundryItems.Add(item);
        await _db.SaveChangesAsync();

        _db.LaundryItemEvents.Add(new LaundryItemEvent
        {
            ItemId    = item.id,
            WorkerId  = GetUserId(),
            ToStatus  = "registered",
            Notes     = "Artikel registriran",
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        return Ok(MapItem(item));
    }

    // GET /api/Laundry/items/{id}/events
    [HttpGet("items/{id}/events")]
    public async Task<IActionResult> GetItemEvents(int id)
    {
        if (!await HasPermAsync("laundry.view")) return Forbidden("laundry.view");
        var licId = GetLicenseId();

        if (!await _db.LaundryItems.AnyAsync(i => i.id == id && i.LicenseId == licId))
            return NotFound();

        var events = await _db.LaundryItemEvents
            .AsNoTracking()
            .Where(e => e.ItemId == id)
            .Include(e => e.Worker)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new {
                e.id, e.FromStatus, e.ToStatus, e.Notes, e.CreatedAt,
                WorkerName = e.Worker != null ? e.Worker.Name + " " + e.Worker.Surname : null,
            })
            .ToListAsync();

        return Ok(events);
    }

    // POST /api/Laundry/items/{id}/event  — manual status transition
    [HttpPost("items/{id}/event")]
    public async Task<IActionResult> AddEvent(int id, [FromBody] LaundryEventDto dto)
    {
        if (!await HasPermAsync("laundry.process")) return Forbidden("laundry.process");
        var licId = GetLicenseId();

        var item = await _db.LaundryItems.FirstOrDefaultAsync(i => i.id == id && i.LicenseId == licId);
        if (item == null) return NotFound();

        var ev = new LaundryItemEvent
        {
            ItemId     = id,
            WorkerId   = GetUserId(),
            FromStatus = item.Status,
            ToStatus   = dto.ToStatus,
            Notes      = dto.Notes,
            CreatedAt  = DateTime.UtcNow,
        };
        _db.LaundryItemEvents.Add(ev);

        // Update item.Status for terminal states
        if (dto.ToStatus == LaundryStatus.WrittenOff)
            item.Status = "written_off";

        await _db.SaveChangesAsync();
        return Ok(new { message = "Status posodobljen.", toStatus = dto.ToStatus });
    }

    // ══════════════════════════════════════════════════════════════════════
    // BINS
    // ══════════════════════════════════════════════════════════════════════

    // GET /api/Laundry/bins
    [HttpGet("bins")]
    public async Task<IActionResult> GetBins()
    {
        if (!await HasPermAsync("laundry.view")) return Forbidden("laundry.view");
        var licId = GetLicenseId();

        var bins = await _db.LaundryBins
            .AsNoTracking()
            .Where(b => b.LicenseId == licId)
            .Include(b => b.OpenedBy)
            .Include(b => b.Items)
            .OrderByDescending(b => b.OpenedAt)
            .ToListAsync();

        return Ok(bins.Select(b => new
        {
            b.id, b.Label, b.Status, b.OpenedAt, b.ClosedAt,
            OpenedByName = b.OpenedBy != null ? b.OpenedBy.Name + " " + b.OpenedBy.Surname : null,
            ItemCount = b.Items.Count,
        }));
    }

    // POST /api/Laundry/bins
    [HttpPost("bins")]
    public async Task<IActionResult> CreateBin([FromBody] LaundryBinCreateDto dto)
    {
        if (!await HasPermAsync("laundry.deposit")) return Forbidden("laundry.deposit");
        var licId = GetLicenseId();
        if (!licId.HasValue) return Unauthorized();

        var bin = new LaundryBin
        {
            LicenseId      = licId.Value,
            Label          = dto.Label,
            Status         = "open",
            OpenedAt       = DateTime.UtcNow,
            OpenedByUserId = GetUserId(),
        };
        _db.LaundryBins.Add(bin);
        await _db.SaveChangesAsync();
        return Ok(new { bin.id, bin.Label, bin.Status, bin.OpenedAt });
    }

    // POST /api/Laundry/bins/{id}/scan  — scan item into bin
    [HttpPost("bins/{id}/scan")]
    public async Task<IActionResult> ScanIntoBin(int id, [FromBody] LaundryScanDto dto)
    {
        if (!await HasPermAsync("laundry.deposit")) return Forbidden("laundry.deposit");
        var licId = GetLicenseId();

        var bin = await _db.LaundryBins.FirstOrDefaultAsync(b => b.id == id && b.LicenseId == licId);
        if (bin == null) return NotFound(new { message = "Zabojnik ni najden." });
        if (bin.Status != "open") return BadRequest(new { message = "Zabojnik ni odprt." });

        // Resolve item by EPC
        var tag = await _db.TAG.AsNoTracking()
            .FirstOrDefaultAsync(t => (t.Epc == dto.Epc || t.EpcAscii == dto.Epc) && t.LicenseId == licId);
        if (tag == null) return NotFound(new { message = "Tag ni najden." });

        var item = await _db.LaundryItems
            .FirstOrDefaultAsync(i => i.TagId == tag.id && i.LicenseId == licId);
        if (item == null) return NotFound(new { message = "Artikel pralnice ni registriran za ta tag." });

        // Avoid duplicates in same bin
        if (await _db.LaundryBinItems.AnyAsync(bi => bi.BinId == id && bi.ItemId == item.id))
            return Conflict(new { message = "Artikel je že v tem zabojniku." });

        _db.LaundryBinItems.Add(new LaundryBinItem
        {
            BinId           = id,
            ItemId          = item.id,
            ScannedAt       = DateTime.UtcNow,
            ScannedByUserId = GetUserId(),
        });
        _db.LaundryItemEvents.Add(new LaundryItemEvent
        {
            ItemId    = item.id,
            WorkerId  = GetUserId(),
            FromStatus = item.Status,
            ToStatus  = LaundryStatus.Deposited,
            Notes     = $"Oddan v zabojnik: {bin.Label}",
            CreatedAt = DateTime.UtcNow,
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Artikel dodan v zabojnik.", itemId = item.id, itemName = item.Name });
    }

    // POST /api/Laundry/bins/{id}/send-to-wash
    [HttpPost("bins/{id}/send-to-wash")]
    public async Task<IActionResult> SendToWash(int id)
    {
        if (!await HasPermAsync("laundry.process")) return Forbidden("laundry.process");
        var licId = GetLicenseId();

        var bin = await _db.LaundryBins
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.id == id && b.LicenseId == licId);
        if (bin == null) return NotFound();
        if (bin.Status != "open") return BadRequest(new { message = "Zabojnik ni v stanju 'open'." });

        bin.Status   = "in_wash";
        bin.ClosedAt = DateTime.UtcNow;

        var itemIds = bin.Items.Select(i => i.ItemId).ToList();
        var items = await _db.LaundryItems.Where(i => itemIds.Contains(i.id)).ToListAsync();
        var workerId = GetUserId();
        foreach (var item in items)
        {
            _db.LaundryItemEvents.Add(new LaundryItemEvent
            {
                ItemId    = item.id, WorkerId  = workerId,
                FromStatus = item.Status, ToStatus  = LaundryStatus.InWash,
                Notes     = $"Zabojnik {bin.Label} poslan v pranje", CreatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = $"Zabojnik poslan v pranje. Artiklov: {items.Count}" });
    }

    // ══════════════════════════════════════════════════════════════════════
    // PROCESSING — scan item and update its status at any station
    // ══════════════════════════════════════════════════════════════════════

    // POST /api/Laundry/process  — universal status update (ironing, sewing, etc.)
    [HttpPost("process")]
    public async Task<IActionResult> Process([FromBody] LaundryProcessDto dto)
    {
        if (!await HasPermAsync("laundry.process")) return Forbidden("laundry.process");
        var licId = GetLicenseId();

        LaundryItem? item = null;

        if (dto.ItemId.HasValue)
        {
            item = await _db.LaundryItems.FirstOrDefaultAsync(i => i.id == dto.ItemId && i.LicenseId == licId);
        }
        else if (!string.IsNullOrWhiteSpace(dto.Epc))
        {
            var tag = await _db.TAG.AsNoTracking()
                .FirstOrDefaultAsync(t => (t.Epc == dto.Epc || t.EpcAscii == dto.Epc) && t.LicenseId == licId);
            if (tag != null)
                item = await _db.LaundryItems.FirstOrDefaultAsync(i => i.TagId == tag.id && i.LicenseId == licId);
        }

        if (item == null) return NotFound(new { message = "Artikel ni najden." });

        var ev = new LaundryItemEvent
        {
            ItemId     = item.id,
            WorkerId   = GetUserId(),
            FromStatus = item.Status,
            ToStatus   = dto.ToStatus,
            Notes      = dto.Notes,
            CreatedAt  = DateTime.UtcNow,
        };
        _db.LaundryItemEvents.Add(ev);

        if (dto.ToStatus == LaundryStatus.WrittenOff)
            item.Status = "written_off";

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message    = "Status posodobljen.",
            itemId     = item.id,
            itemName   = item.Name,
            fromStatus = ev.FromStatus,
            toStatus   = ev.ToStatus,
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    // SETS (kompleti)
    // ══════════════════════════════════════════════════════════════════════

    // GET /api/Laundry/sets
    [HttpGet("sets")]
    public async Task<IActionResult> GetSets([FromQuery] string? status = null)
    {
        if (!await HasPermAsync("laundry.view")) return Forbidden("laundry.view");
        var licId = GetLicenseId();

        var q = _db.LaundrySets.AsNoTracking()
            .Where(s => s.LicenseId == licId)
            .Include(s => s.Owner)
            .Include(s => s.AssembledBy)
            .Include(s => s.Items).ThenInclude(si => si.Item)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(s => s.Status == status);

        var sets = await q.OrderByDescending(s => s.AssembledAt).ToListAsync();

        return Ok(sets.Select(s => new
        {
            s.id, s.Status, s.AssembledAt, s.PickedUpAt,
            OwnerName       = s.Owner != null ? s.Owner.Name + " " + s.Owner.Surname : null,
            AssembledByName = s.AssembledBy != null ? s.AssembledBy.Name + " " + s.AssembledBy.Surname : null,
            Items = s.Items.Select(si => new { si.Item!.id, si.Item.Name, si.Item.Category }),
        }));
    }

    // POST /api/Laundry/sets
    [HttpPost("sets")]
    public async Task<IActionResult> CreateSet([FromBody] LaundrySetCreateDto dto)
    {
        if (!await HasPermAsync("laundry.process")) return Forbidden("laundry.process");
        var licId = GetLicenseId();
        if (!licId.HasValue) return Unauthorized();

        var set = new LaundrySet
        {
            LicenseId         = licId.Value,
            OwnerId           = dto.OwnerId,
            AssembledByUserId = GetUserId(),
            AssembledAt       = DateTime.UtcNow,
            Status            = "ready",
        };
        _db.LaundrySets.Add(set);
        await _db.SaveChangesAsync();

        foreach (var itemId in dto.ItemIds)
        {
            var item = await _db.LaundryItems.FirstOrDefaultAsync(i => i.id == itemId && i.LicenseId == licId);
            if (item == null) continue;

            _db.LaundrySetItems.Add(new LaundrySetItem { SetId = set.id, ItemId = itemId });
            _db.LaundryItemEvents.Add(new LaundryItemEvent
            {
                ItemId    = itemId, WorkerId  = GetUserId(),
                FromStatus = item.Status, ToStatus  = LaundryStatus.InSet,
                Notes     = $"Vključen v komplet #{set.id}", CreatedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync();
        return Ok(new { set.id, set.Status, set.AssembledAt });
    }

    // POST /api/Laundry/sets/{id}/pickup
    [HttpPost("sets/{id}/pickup")]
    public async Task<IActionResult> Pickup(int id)
    {
        if (!await HasPermAsync("laundry.deposit")) return Forbidden("laundry.deposit");
        var licId = GetLicenseId();

        var set = await _db.LaundrySets
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.id == id && s.LicenseId == licId);
        if (set == null) return NotFound();
        if (set.Status != "ready") return BadRequest(new { message = "Komplet ni v stanju 'ready'." });

        set.Status          = "picked_up";
        set.PickedUpAt      = DateTime.UtcNow;
        set.PickedUpByUserId = GetUserId();

        var workerId = GetUserId();
        foreach (var si in set.Items)
        {
            _db.LaundryItemEvents.Add(new LaundryItemEvent
            {
                ItemId    = si.ItemId, WorkerId  = workerId,
                FromStatus = LaundryStatus.InSet, ToStatus  = LaundryStatus.PickedUp,
                Notes     = $"Prevzeto iz kompleta #{id}", CreatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Komplet prevzet." });
    }

    // ══════════════════════════════════════════════════════════════════════
    // DASHBOARD — current item status overview
    // ══════════════════════════════════════════════════════════════════════

    // GET /api/Laundry/dashboard
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        if (!await HasPermAsync("laundry.view")) return Forbidden("laundry.view");
        var licId = GetLicenseId();

        var latestStatuses = await _db.LaundryItemEvents
            .AsNoTracking()
            .Where(e => _db.LaundryItems
                .Where(i => i.LicenseId == licId && i.Status == "active")
                .Select(i => i.id)
                .Contains(e.ItemId))
            .GroupBy(e => e.ItemId)
            .Select(g => new { ItemId = g.Key, ToStatus = g.OrderByDescending(e => e.CreatedAt).First().ToStatus })
            .ToListAsync();

        var counts = latestStatuses
            .GroupBy(x => x.ToStatus)
            .ToDictionary(g => g.Key, g => g.Count());

        var openBins   = await _db.LaundryBins.CountAsync(b => b.LicenseId == licId && b.Status == "open");
        var readySets  = await _db.LaundrySets.CountAsync(s => s.LicenseId == licId && s.Status == "ready");
        var writeOffs  = await _db.LaundryItems.CountAsync(i => i.LicenseId == licId && i.Status == "written_off");

        return Ok(new { counts, openBins, readySets, writeOffs });
    }

    // ── Mapping helper ─────────────────────────────────────────────────────
    private static object MapItem(LaundryItem i) => new
    {
        i.id, i.Name, i.Category, i.Status, i.Notes, i.CreatedAt,
        OwnerId   = i.OwnerId,
        OwnerName = i.Owner != null ? i.Owner.Name + " " + i.Owner.Surname : null,
        TagId     = i.TagId,
        Epc       = i.Tag?.Epc,
        EpcAscii  = i.Tag?.EpcAscii,
    };
}

// ── DTOs ───────────────────────────────────────────────────────────────────
public record LaundryItemCreateDto(string Name, string? Category, int? OwnerId, int? TagId, string? Notes);
public record LaundryBinCreateDto(string Label);
public record LaundryScanDto(string Epc);
public record LaundryEventDto(string ToStatus, string? Notes);
public record LaundryProcessDto(string ToStatus, string? Epc, int? ItemId, string? Notes);
public record LaundrySetCreateDto(int OwnerId, List<int> ItemIds);
