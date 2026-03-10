using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Signalko.Infrastructure;
using Signalko.Core;
using Signalko.Web.Services;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/hooks")]
public class HooksController : ControllerBase
{
    private readonly AppDbContext _db;
    public HooksController(AppDbContext db) => _db = db;

    [HttpPost("zebra")]
    public async Task<IActionResult> Zebra([FromBody] JsonNode body)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        // ── 1. Preberi vse kandidate iz payload-a (brez DB klicev) ──────────
        var candidates = new List<(string Epc, int? Antenna, int? Rssi, int? Reads,
                                   string? Hostname, string? ReaderIp, DateTime? Time)>();
        try
        {
            if (body is JsonArray arrFx)
            {
                foreach (var node in arrFx)
                {
                    if (node is not JsonObject obj) continue;
                    var data = obj["data"] as JsonObject;
                    if (data is null) continue;
                    var epc = (string?)data["idHex"];
                    if (string.IsNullOrWhiteSpace(epc)) continue;
                    candidates.Add((
                        epc,
                        data["antenna"]?.GetValue<int?>(),
                        data["peakRssi"]?.GetValue<int?>(),
                        data["reads"]?.GetValue<int?>(),
                        (string?)data["hostName"],
                        (string?)data["readerIp"] ?? remoteIp,
                        ParseFxTimestampToUtc((string?)obj["timestamp"])
                    ));
                }
            }
            else
            {
                string? hostRoot = (string?)(body["hostname"] ?? body["readerName"] ?? body["host"] ?? body["readerHostname"]);
                JsonArray? arr = body["tags"] as JsonArray ?? body["tagData"] as JsonArray
                              ?? body["tagReportData"] as JsonArray ?? body["data"] as JsonArray;
                if (arr != null)
                {
                    foreach (var n in arr)
                    {
                        if (n is not JsonObject o) continue;
                        var epc = (string?)(o["epc"] ?? o["EPC"] ?? o["EPC-96"]);
                        if (string.IsNullOrWhiteSpace(epc)) continue;
                        candidates.Add((
                            epc!,
                            o["antenna"]?.GetValue<int?>() ?? o["antennaID"]?.GetValue<int?>(),
                            o["rssi"]?.GetValue<int?>() ?? o["RSSI"]?.GetValue<int?>()
                                ?? o["peakRssi"]?.GetValue<int?>() ?? o["peakRSSI"]?.GetValue<int?>(),
                            1,
                            (string?)(o["hostname"] ?? o["readerName"]) ?? hostRoot,
                            (string?)o["readerIp"] ?? remoteIp,
                            ParseFxTimestampToUtc((string?)(o["timestamp"] ?? o["time"] ?? o["firstSeen"]))
                        ));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Problem(title: "Parsing error", detail: ex.Message);
        }

        if (candidates.Count == 0)
            return Ok(new { inserted = 0, note = "Empty payload." });

        // ── 2. Ena batch poizvedba za znane readerje ────────────────────────
        var hostnames  = candidates.Select(c => c.Hostname).Where(h => !string.IsNullOrEmpty(h)).Distinct().ToList();
        var ips        = candidates.Select(c => c.ReaderIp).Where(i => !string.IsNullOrEmpty(i)).Distinct().ToList();

        var knownHosts = await _db.readers.AsNoTracking()
            .Where(r => r.Hostname != null && hostnames.Contains(r.Hostname))
            .Select(r => r.Hostname!).ToHashSetAsync();

        var knownIps = await _db.readers.AsNoTracking()
            .Where(r => r.IP != null && ips.Contains(r.IP))
            .Select(r => r.IP!).ToHashSetAsync();

        // ── 3. Filtriraj in sestavi TAG objekte ─────────────────────────────
        var list = new List<Tag>();
        foreach (var c in candidates)
        {
            bool known = (!string.IsNullOrEmpty(c.Hostname) && knownHosts.Contains(c.Hostname!)) ||
                         (!string.IsNullOrEmpty(c.ReaderIp) && knownIps.Contains(c.ReaderIp!));
            if (!known) continue;

            list.Add(new Tag
            {
                Epc        = c.Epc,
                EpcAscii   = HexUtil.HexToAsciiStrict(c.Epc),
                Time       = c.Time ?? DateTime.UtcNow,
                Antenna    = c.Antenna,
                RSSI       = c.Rssi,
                SEEN_COUNT = c.Reads ?? 1,
                ReaderIP   = c.ReaderIp,
                Hostname   = c.Hostname,
            });
        }

        if (list.Count == 0)
            return Ok(new { inserted = 0, note = "No accepted tags (unknown reader hostname/IP)." });

        _db.TAG.AddRange(list);
        await _db.SaveChangesAsync();
        return Ok(new { inserted = list.Count });
    }

    private static DateTime? ParseFxTimestampToUtc(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Regex.IsMatch(raw, @"[+-]\d{4}$"))
            raw = raw[..^5] + raw[^5..^2] + ":" + raw[^2..];
        if (DateTimeOffset.TryParse(raw, out var dto)) return dto.UtcDateTime;
        var fmts = new[]
        {
            "yyyy-MM-dd'T'HH:mm:ss.fffK", "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.fffzzz", "yyyy-MM-dd'T'HH:mm:sszzz"
        };
        foreach (var f in fmts)
            if (DateTimeOffset.TryParseExact(raw, f, null, System.Globalization.DateTimeStyles.None, out dto))
                return dto.UtcDateTime;
        return null;
    }
}
