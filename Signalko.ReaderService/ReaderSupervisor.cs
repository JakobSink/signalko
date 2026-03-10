using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Signalko.Infrastructure;
using Signalko.Infrastructure.Services;
using Signalko.Core;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace Signalko.Web.Services;

public class ReaderSupervisor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReaderSupervisor> _logger;

    private readonly ConcurrentDictionary<string, int> _failCounts = new();
    private readonly ConcurrentDictionary<string, int> _okCounts = new();

    private const int FAIL_THRESHOLD = 3; // po 3x fail -> Enabled = 0
    private const int OK_THRESHOLD   = 2; // po 2x OK   -> Enabled = 1

    // kako pogosto teče zanka (ping + branje tagov)
    private static readonly TimeSpan LoopDelay = TimeSpan.FromSeconds(3); // 🔁 vsakih 3s

    public ReaderSupervisor(IServiceScopeFactory scopeFactory, ILogger<ReaderSupervisor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Model za en odčitek taga iz readerja
    private class TagRead
    {
        public string Epc { get; set; } = string.Empty;
        public int AntennaPort { get; set; }
        public int? Rssi { get; set; }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("📡 ReaderSupervisor zagnan (ping + branje tagov).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var tagService = scope.ServiceProvider.GetRequiredService<TagService>();

                // preberemo vse readerje (lahko tudi samo Enabled, če želiš)
                var readers = await db.readers.AsNoTracking().ToListAsync(stoppingToken);

                foreach (var r in readers)
                {
                    string key = r.IP ?? $"#{r.id}";

                    bool reachable =
                        await IsReachable(r.IP, 80,  TimeSpan.FromMilliseconds(1200)) ||
                        await IsReachable(r.IP, 443, TimeSpan.FromMilliseconds(1200));

                    if (reachable)
                    {
                        _okCounts.AddOrUpdate(key, 1, (_, v) => v + 1);
                        _failCounts.AddOrUpdate(key, 0, (_, __) => 0);

                        _logger.LogDebug("✅ {Name} ({IP}) dosegljiv.", r.Name ?? "Reader", r.IP);

                        // po nekaj OK-jih ponovno omogočimo, če je bil Disabled
                        if (_okCounts[key] >= OK_THRESHOLD && !r.Enabled)
                        {
                            await SetEnabledAsync(db, r.id, true, stoppingToken);
                            _logger.LogInformation("🔁 {Name} ({IP}) ponovno omogočen (Enabled=1).", r.Name ?? "Reader", r.IP);
                            _okCounts[key] = 0;
                        }

                        // 🔄 TUKAJ: branje tagov in zapis v bazo
                        if (r.Enabled)
                        {
                            await PollTagsForReaderAsync(db, tagService, r, stoppingToken);
                        }
                    }
                    else
                    {
                        _failCounts.AddOrUpdate(key, 1, (_, v) => v + 1);
                        _okCounts.AddOrUpdate(key, 0, (_, __) => 0);

                        _logger.LogWarning("❌ {Name} ({IP}) NI dosegljiv.", r.Name ?? "Reader", r.IP);

                        if (_failCounts[key] >= FAIL_THRESHOLD && r.Enabled)
                        {
                            await SetEnabledAsync(db, r.id, false, stoppingToken);
                            _logger.LogWarning("⛔ {Name} ({IP}) onemogočen (Enabled=0) po {N} neuspehih.", r.Name ?? "Reader", r.IP, _failCounts[key]);
                            _failCounts[key] = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Napaka v ReaderSupervisor zanki.");
            }

            await Task.Delay(LoopDelay, stoppingToken); // 🔁 npr. 3s
        }
    }

    private static async Task<bool> IsReachable(string? host, int port, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connectTask, Task.Delay(timeout));
            return done == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task SetEnabledAsync(AppDbContext db, int readerId, bool enabled, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE readers SET Enabled = {0} WHERE id = {1}",
            parameters: new object[] { enabled ? 1 : 0, readerId },
            cancellationToken: ct);
    }

    /// <summary>
    /// Tukaj ZA RES preberemo tage iz readerja in jih preko TagService.UpsertTagAsync vpišemo v bazo.
    /// </summary>
    private async Task PollTagsForReaderAsync(
        AppDbContext db,
        TagService tagService,
        Reader reader,
        CancellationToken ct)
    {
        // 1) preberemo antene za ta reader (da iz porta pridemo do antennas.id)
        var antennas = await db.antennas
            .Where(a => a.ReaderId == reader.id)
            .ToListAsync(ct);

        // TODO: tukaj vstavljaš SVOJ klic na reader (HTTP/SDK), ki vrne tag-e.
        var reads = await ReadTagsFromReaderAsync(reader.IP!, ct);

        foreach (var read in reads)
        {
            // najdi antennas.id za ta reader + port
            var antenna = antennas.FirstOrDefault(a => a.Port == read.AntennaPort);
            if (antenna == null)
                continue;

            await tagService.UpsertTagAsync(
                epc: read.Epc,
                antennaId: antenna.id,
                readerIp: reader.IP,
                hostname: reader.Hostname,
                rssi: read.Rssi
            );
        }
    }

    /// <summary>
    /// PSEVDO-funkcija: tukaj uporabi svojo kodo, ki bere iz readerja.
    /// </summary>
    private async Task<List<TagRead>> ReadTagsFromReaderAsync(string readerIp, CancellationToken ct)
    {
        // 🔴 TODO:
        // tukaj uporabiš tisti HttpClient klic / SDK klic, ki ga že imaš,
        // npr. GET http://{readerIp}/api/tags, potem deserijaliziraš JSON v List<TagRead>.

        await Task.CompletedTask;
        return new List<TagRead>();
    }
}
