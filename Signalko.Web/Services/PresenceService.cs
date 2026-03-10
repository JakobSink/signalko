using Microsoft.EntityFrameworkCore;
using Signalko.Core;
using Signalko.Infrastructure;

namespace Signalko.Web.Services;

/// <summary>
/// Procesira prisotnost: ko antena v prisotnostni coni prebere EPC kartice,
/// zabeleži IN/OUT glede na tip cone (Entrance / Exit / EntranceExit).
/// </summary>
public class PresenceService
{
    // Tipi con, ki sprožijo prisotnostno beleženje
    private static readonly HashSet<string> PresenceZoneTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Entrance", "Exit", "EntranceExit" };

    // Čas med dvema zaporednima enakim eventoma (prepreči dvojno beleženje)
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _db;

    public PresenceService(AppDbContext db) => _db = db;

    /// <summary>
    /// Pokliči po vsakem prejetem tagu. Ignorira tage, ki ne ustrezajo prisotnostni logiki.
    /// </summary>
    public async Task ProcessTagAsync(string epc, string? readerIp, string? hostname, int? antennaPort)
    {
        if (string.IsNullOrWhiteSpace(epc) || antennaPort == null) return;

        // 1. Poišči anteno prek IP/hostname čitalca + port
        var antenna = await _db.antennas
            .AsNoTracking()
            .Include(a => a.Zone)
            .Include(a => a.Reader)
            .FirstOrDefaultAsync(a =>
                a.Port == antennaPort &&
                a.Reader != null &&
                (a.Reader.IP == readerIp ||
                 (!string.IsNullOrEmpty(hostname) && a.Reader.Hostname == hostname)));

        if (antenna?.Zone == null) return;

        var zoneType = antenna.Zone.Type ?? "";
        if (!PresenceZoneTypes.Contains(zoneType)) return;

        // 2. Poišči uporabnika prek CardEpc
        var user = await _db.users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.CardEpc == epc);

        if (user == null) return;

        // 3. Določi tip dogodka
        string eventType;

        if (zoneType.Equals("Entrance", StringComparison.OrdinalIgnoreCase))
        {
            eventType = "IN";
        }
        else if (zoneType.Equals("Exit", StringComparison.OrdinalIgnoreCase))
        {
            eventType = "OUT";
        }
        else // EntranceExit — toggle glede na zadnji event tega userja
        {
            var last = await _db.UserPresences
                .AsNoTracking()
                .Where(p => p.UserId == user.id)
                .OrderByDescending(p => p.ScannedAt)
                .Select(p => p.Type)
                .FirstOrDefaultAsync();

            eventType = last == "IN" ? "OUT" : "IN";
        }

        // 4. Throttle — enaka zona + tip v zadnjih N minutah → preskoči
        var since = DateTime.UtcNow - ThrottleWindow;
        bool recent = await _db.UserPresences
            .AsNoTracking()
            .AnyAsync(p =>
                p.UserId == user.id &&
                p.Type   == eventType &&
                p.ZoneId == antenna.ZoneId &&
                p.ScannedAt >= since);

        if (recent) return;

        // 5. Zabeleži prisotnost
        _db.UserPresences.Add(new UserPresence
        {
            UserId    = user.id,
            Type      = eventType,
            ScannedAt = DateTime.UtcNow,
            ZoneId    = antenna.ZoneId == 0 ? null : antenna.ZoneId,
            AntennaId = antenna.id,
        });

        await _db.SaveChangesAsync();
    }
}
