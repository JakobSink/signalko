using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Contracts;
using Signalko.Web.Services;
using System.Text;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssetController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public AssetController(AppDbContext db, IWebHostEnvironment env)
    {
        _db  = db;
        _env = env;
    }

    // ── GET /api/Asset ────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAssets()
    {
        // One-time backfill: set EpcAscii for any tag that still has it empty
        var tagsToFix = await _db.TAG
            .Where(t => !string.IsNullOrEmpty(t.Epc) && string.IsNullOrEmpty(t.EpcAscii))
            .ToListAsync();

        foreach (var tag in tagsToFix)
            tag.EpcAscii = HexUtil.HexToAsciiLenient(tag.Epc) ?? tag.Epc;

        if (tagsToFix.Count > 0)
            await _db.SaveChangesAsync();

        // Normal load
        var assets = await _db.ASSET
            .Include(a => a.Author)
            .Include(a => a.Tag)
            .AsNoTracking()
            .ToListAsync();

        var activeLoans = await _db.assets_loans
            .Where(l => l.ReturnedAt == null)
            .Select(l => new { l.AssetId, l.id })
            .ToListAsync();

        var result = assets.Select(a => new
        {
            a.id, a.Name, a.Details, a.Icon,
            Tag        = a.Tag == null ? null : new { a.Tag.id, a.Tag.Epc, a.Tag.EpcAscii },
            ActiveLoan = activeLoans.Any(l => l.AssetId == a.id),
        });

        return Ok(result);
    }

    // ── GET /api/Asset/{id} ───────────────────────────────────────────────────
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAsset(int id)
    {
        var a = await _db.ASSET
            .Include(x => x.Author)
            .Include(x => x.Tag)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.id == id);

        return a == null ? NotFound() : Ok(a);
    }

    // ── GET /api/Asset/by-epc/{epc} ───────────────────────────────────────────
    [HttpGet("by-epc/{epc}")]
    public async Task<IActionResult> GetByEpc(string epc)
    {
        var tag = await _db.TAG.AsNoTracking().FirstOrDefaultAsync(t => t.Epc == epc);
        if (tag == null) return NotFound();

        var asset = await _db.ASSET
            .Include(a => a.Author)
            .Include(a => a.Tag)
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.TagId == tag.id);

        return asset == null ? NotFound() : Ok(asset);
    }

    // ── POST /api/Asset ───────────────────────────────────────────────────────
    [HttpPost, Authorize]
    public async Task<IActionResult> AddAsset([FromBody] AssetUpsertDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();
        int? tagId = dto.TagId;

        if (!string.IsNullOrWhiteSpace(dto.Epc))
        {
            var tag = await _db.TAG.FirstOrDefaultAsync(t => t.Epc == dto.Epc);
            if (tag == null)
            {
                tag = new Tag { Epc = dto.Epc };
                _db.TAG.Add(tag);
            }
            // Always fill/override EpcAscii — prefer explicitly supplied, else auto-convert
            tag.EpcAscii = !string.IsNullOrWhiteSpace(dto.EpcAscii)
                ? dto.EpcAscii
                : HexUtil.HexToAsciiLenient(dto.Epc) ?? dto.Epc;

            await _db.SaveChangesAsync();
            tagId = tag.id;
        }

        var entity = new Asset
        {
            Name    = dto.Name,
            Details = dto.Description,
            TagId   = tagId,
            Icon    = dto.Icon,
        };
        _db.ASSET.Add(entity);
        await _db.SaveChangesAsync();

        await _db.Entry(entity).Reference(a => a.Tag).LoadAsync();
        return CreatedAtAction(nameof(GetAsset), new { id = entity.id }, entity);
    }

    // ── PUT /api/Asset/{id} ───────────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsset(int id, [FromBody] AssetUpsertDto dto)
    {
        var entity = await _db.ASSET.Include(a => a.Tag).FirstOrDefaultAsync(a => a.id == id);
        if (entity == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.Name))        entity.Name    = dto.Name;
        if (dto.Description != null)                     entity.Details = dto.Description;
        if (dto.AuthorId.HasValue)                       entity.AuthorId = dto.AuthorId;
        if (dto.Icon != null)                            entity.Icon    = dto.Icon == "" ? null : dto.Icon;

        if (!string.IsNullOrWhiteSpace(dto.Epc))
        {
            var tag = await _db.TAG.FirstOrDefaultAsync(t => t.Epc == dto.Epc);
            if (tag == null)
            {
                tag = new Tag { Epc = dto.Epc };
                _db.TAG.Add(tag);
            }
            // Prefer explicitly supplied EpcAscii; else auto-convert; else keep existing
            tag.EpcAscii = !string.IsNullOrWhiteSpace(dto.EpcAscii)
                ? dto.EpcAscii
                : HexUtil.HexToAsciiLenient(dto.Epc) ?? tag.EpcAscii ?? dto.Epc;

            await _db.SaveChangesAsync();
            entity.TagId = tag.id;
        }
        else if (dto.TagId.HasValue)
        {
            entity.TagId = dto.TagId;
        }
        else if (!string.IsNullOrWhiteSpace(dto.EpcAscii) && entity.Tag != null)
        {
            // EpcAscii changed without changing the EPC hex
            entity.Tag.EpcAscii = dto.EpcAscii;
        }

        await _db.SaveChangesAsync();
        await _db.Entry(entity).Reference(a => a.Tag).LoadAsync();
        return Ok(entity);
    }

    // ── POST /api/Asset/{id}/icon ─────────────────────────────────────────────
    [HttpPost("{id:int}/icon")]
    public async Task<IActionResult> UploadIcon(int id, IFormFile file)
    {
        var entity = await _db.ASSET.FindAsync(id);
        if (entity == null) return NotFound();

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" }.Contains(ext))
            return BadRequest(new { message = "Dovoljeni formati: jpg, jpeg, png, gif, webp." });

        var dir = Path.Combine(_env.WebRootPath, "assets-img");
        Directory.CreateDirectory(dir);

        foreach (var old in Directory.GetFiles(dir, $"{id}.*"))
            System.IO.File.Delete(old);

        var fileName = $"{id}{ext}";
        var path     = Path.Combine(dir, fileName);
        using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream);

        entity.Icon = $"/assets-img/{fileName}";
        await _db.SaveChangesAsync();

        return Ok(new { icon = entity.Icon });
    }

    // ── DELETE /api/Asset/{id} ────────────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsset(int id)
    {
        var entity = await _db.ASSET.FirstOrDefaultAsync(a => a.id == id);
        if (entity == null) return NotFound();
        _db.ASSET.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ── GET /api/Asset/template ───────────────────────────────────────────────
    [HttpGet("template")]
    public IActionResult DownloadTemplate()
    {
        var bytes = BuildWorkbook(Array.Empty<(string, string, string, string)>());
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "sredstva-predloga.xlsx");
    }

    // ── GET /api/Asset/export ─────────────────────────────────────────────────
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        // Single query — join ASSET + TAG in DB, no per-row round-trips
        var rows = await _db.ASSET
            .AsNoTracking()
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                a.Name, a.Details,
                Epc      = a.Tag != null ? a.Tag.Epc      : null,
                EpcAscii = a.Tag != null ? a.Tag.EpcAscii : null,
            })
            .ToListAsync();

        var bytes = BuildWorkbook(rows.Select(r =>
            (r.Name ?? "", r.Details ?? "", r.Epc ?? "", r.EpcAscii ?? "")));

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"sredstva-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    // ── POST /api/Asset/import ────────────────────────────────────────────────
    // Row 1 = header (skipped). Batches all DB reads before writing.
    [HttpPost("import"), Authorize]
    public async Task<IActionResult> Import(IFormFile file)
    {
        if (!await IsAdminAsync()) return Forbid();
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Datoteka je prazna." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx" && ext != ".xls")
            return BadRequest(new { message = "Dovoljeni formati: .xlsx, .xls" });

        // ── 1. Parse Excel in memory ──────────────────────────────────────────
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;

        using var wb  = new XLWorkbook(ms);
        var ws        = wb.Worksheet(1);
        var lastRow   = ws.LastRowUsed()?.RowNumber() ?? 1;

        var validRows = new List<(int Num, string Name, string Details, string EpcHex, string EpcAscii)>();
        var errors    = new List<string>();
        int skipped   = 0;

        for (int r = 2; r <= lastRow; r++)
        {
            var name     = ws.Cell(r, 1).GetValue<string>().Trim();
            var details  = ws.Cell(r, 2).GetValue<string>().Trim();
            var epcHex   = ws.Cell(r, 3).GetValue<string>().Trim();
            var epcAscii = ws.Cell(r, 4).GetValue<string>().Trim();

            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(epcHex)) { skipped++; continue; }
            if (string.IsNullOrEmpty(name))   { errors.Add($"Vrstica {r}: ime je obvezno.");    skipped++; continue; }
            if (string.IsNullOrEmpty(epcHex)) { errors.Add($"Vrstica {r}: EPC hex je obvezen."); skipped++; continue; }

            if (string.IsNullOrEmpty(epcAscii))
                epcAscii = HexUtil.HexToAsciiLenient(epcHex) ?? epcHex;

            validRows.Add((r, name, details, epcHex, epcAscii));
        }

        if (validRows.Count == 0)
            return Ok(new { created = 0, updated = 0, skipped, errors });

        // ── 2. Batch-load all matching Tags and Assets in two queries ─────────
        var allEpcs    = validRows.Select(r => r.EpcHex).Distinct().ToList();
        var existTags  = await _db.TAG
            .Where(t => allEpcs.Contains(t.Epc!))
            .ToDictionaryAsync(t => t.Epc!);

        var existTagIds = existTags.Values.Select(t => t.id).ToList();
        var existAssets = await _db.ASSET
            .Where(a => a.TagId != null && existTagIds.Contains(a.TagId!.Value))
            .ToDictionaryAsync(a => a.TagId!.Value);

        // ── 3. Apply changes in memory, single SaveChangesAsync ───────────────
        int created = 0, updated = 0;

        foreach (var (_, rowName, rowDetails, rowEpcHex, rowEpcAscii) in validRows)
        {
            Tag tag;
            if (existTags.TryGetValue(rowEpcHex, out var found))
            {
                tag          = found;
                tag.EpcAscii = rowEpcAscii;
            }
            else
            {
                tag = new Tag { Epc = rowEpcHex, EpcAscii = rowEpcAscii };
                _db.TAG.Add(tag);
                existTags[rowEpcHex] = tag;
            }

            if (tag.id != 0 && existAssets.TryGetValue(tag.id, out var asset))
            {
                asset.Name    = rowName;
                if (rowDetails.Length > 0) asset.Details = rowDetails;
                updated++;
            }
            else
            {
                var newAsset = new Asset
                {
                    Name    = rowName,
                    Details = rowDetails.Length > 0 ? rowDetails : null,
                    Tag     = tag,   // EF resolves FK via navigation for new tags
                };
                _db.ASSET.Add(newAsset);
                created++;
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new { created, updated, skipped, errors });
    }

    // ── Excel builder ─────────────────────────────────────────────────────────
    private static readonly string[] ExcelHeaders = ["Ime *", "Opis", "EPC hex *", "EPC ASCII"];

    private static byte[] BuildWorkbook(IEnumerable<(string Name, string Details, string Epc, string EpcAscii)> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sredstva");

        // Header
        for (int c = 1; c <= ExcelHeaders.Length; c++)
            ws.Cell(1, c).Value = ExcelHeaders[c - 1];

        var hdr = ws.Range(1, 1, 1, ExcelHeaders.Length);
        hdr.Style.Font.Bold = true;
        hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // Data
        int row = 2;
        foreach (var (name, details, epc, ascii) in rows)
        {
            ws.Cell(row, 1).Value = name;
            ws.Cell(row, 2).Value = details;
            ws.Cell(row, 3).Value = epc;
            ws.Cell(row, 4).Value = ascii;
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void StyleHeader(IXLWorksheet ws)
    {
        var hdr = ws.Range(1, 1, 1, ExcelHeaders.Length);
        hdr.Style.Font.Bold = true;
        hdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
        hdr.Style.Font.FontColor = XLColor.White;
        hdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    // ── Admin check: parse userId from JWT sub claim, look up role in DB ─────
    private async Task<bool> IsAdminAsync()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var userId)) return false;

        var user = await _db.users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.id == userId);

        return user?.Role?.Name == "Admin";
    }

}
