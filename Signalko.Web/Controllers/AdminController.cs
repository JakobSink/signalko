using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Web.Services;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminController(AppDbContext db) => _db = db;

    /// <summary>Enkratni backfill EpcAscii za vse TAG zapise brez vrednosti.</summary>
    [HttpPost("backfill-epc-ascii")]
    public async Task<IActionResult> BackfillEpcAscii()
    {
        int updated = 0;
        const int pageSize = 1000;
        int page = 0;

        while (true)
        {
            var chunk = await _db.TAG
                .Where(t => t.Epc != null && (t.EpcAscii == null || t.EpcAscii == ""))
                .OrderBy(t => t.id)
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync();

            if (chunk.Count == 0) break;

            foreach (var t in chunk)
            {
                t.EpcAscii = HexUtil.HexToAsciiStrict(t.Epc);
                if (t.EpcAscii != null) updated++;
            }

            await _db.SaveChangesAsync();
            page++;
        }

        return Ok(new { updated });
    }
}
