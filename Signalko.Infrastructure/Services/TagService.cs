using Microsoft.EntityFrameworkCore;
using Signalko.Core;

namespace Signalko.Infrastructure.Services;

public class TagService
{
    private readonly AppDbContext _db;

    public TagService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// UPSERT za TAG: za isti EPC na isti anteni se Time osveži, SEEN_COUNT poveča.
    /// </summary>
    public async Task UpsertTagAsync(string epc, int antennaId, string? readerIp, string? hostname, int? rssi)
    {
        var tag = await _db.TAG
            .FirstOrDefaultAsync(t => t.Epc == epc && t.Antenna == antennaId);

        if (tag == null)
        {
            tag = new Tag
            {
                Epc = epc,
                Antenna = antennaId,
                ReaderIP = readerIp,
                Hostname = hostname,
                Time = DateTime.Now,
                RSSI = rssi,
                SEEN_COUNT = 1
            };
            _db.TAG.Add(tag);
        }
        else
        {
            tag.Time = DateTime.Now; // osveži “živost” taga
            tag.RSSI = rssi;
            tag.SEEN_COUNT = (tag.SEEN_COUNT ?? 0) + 1;
        }

        await _db.SaveChangesAsync();
    }
}
