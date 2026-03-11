using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Web.Services;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TagController : PermissionedController
{
    private readonly PresenceService _presence;
    public TagController(AppDbContext db, PresenceService presence) : base(db)
    {
        _presence = presence;
    }

    // 🇸🇮 Enoten JSON options (če bi kdaj kaj deser. z System.Text.Json)
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // 🇸🇮 Pretvornik HEX -> ASCII (ne-tiskljive znake preskočimo)
    private static string HexToAscii(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return string.Empty;
        var s = hex.Trim();
        Span<char> buf = stackalloc char[s.Length / 2];
        int j = 0;
        for (int i = 0; i + 1 < s.Length; i += 2)
        {
            if (byte.TryParse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                if (b >= 32 && b <= 126) buf[j++] = (char)b; // 🇸🇮 tiskljivi ASCII
            }
        }
        return new string(buf[..j]);
    }

    // 🇸🇮 Parser za Zebra "timestamp" (npr. 2025-10-23T08:06:47.198+0000)
    private static bool TryParseZebraTimestamp(string? s, out DateTime dt)
    {
        dt = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        // 🇸🇮 najprej poskusi z znanimi formati
        string[] formats =
        {
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", // ISO 8601 z (:) v offsetu
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm:ss.fffK",
            "yyyy-MM-dd'T'HH:mm:ssK",
            "yyyy-MM-dd HH:mm:ssK",
            "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
            "yyyy-MM-dd'T'HH:mm:sszzz"
        };

        // 🇸🇮 poseben primer: "+0000" brez dvopičja (dodamo dvopičje pred zadnjima dvema znakoma)
        string normalize(string raw)
        {
            // primer: 2025-10-23T08:06:47.198+0000 -> 2025-10-23T08:06:47.198+00:00
            int plus = raw.LastIndexOf('+');
            int minus = raw.LastIndexOf('-');
            int idx = Math.Max(plus, minus);
            if (idx > 0 && raw.Length - idx == 5) // +HHmm ali -HHmm
            {
                // vstavimo dvopičje: +HH:mm
                return raw.Insert(idx + 3, ":");
            }
            return raw;
        }

        var n = normalize(s);
        return DateTime.TryParseExact(
            n,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out dt
        )
        || DateTime.TryParse(n, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 🇸🇮 Najnovejši raw TAG zapisi (preprosto)
    [HttpGet("latest"), Authorize]
    public async Task<IActionResult> Latest([FromQuery] int take = 50)
    {
        if (!await HasPermAsync("tags.view")) return Forbidden("tags.view");
        return Ok(await _db.TAG.AsNoTracking()
                           .OrderByDescending(t => t.id)
                           .Take(Math.Clamp(take, 1, 500))
                           .ToListAsync());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 🇸🇮 Paged + filtri po stolpcih (za tags.html glavno tabelo)
    // GET /api/tag/grouped-table?page=1&pageSize=20&q=&epc=&ascii=&hostname=&reader=&zone=&antenna=&from=...&to=...&rssiMin=&rssiMax=
    [HttpGet("grouped-table"), Authorize]
    public async Task<IActionResult> GroupedTable(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? q = null,
        [FromQuery] string? epc = null,
        [FromQuery] string? ascii = null,
        [FromQuery] string? hostname = null,
        [FromQuery] string? reader = null,
        [FromQuery] string? zone = null,
        [FromQuery] int? antenna = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? rssiMin = null,
        [FromQuery] int? rssiMax = null)
    {
        if (!await HasPermAsync("tags.view")) return Forbidden("tags.view");
        page = page <= 0 ? 1 : page;
        pageSize = pageSize switch { <= 0 => 20, > 200 => 200, _ => pageSize };
        var skip = (page - 1) * pageSize;

        var cte = @"
WITH latest AS (
    SELECT Epc, MAX(id) AS LastId, COUNT(*) AS Cnt
    FROM TAG
    WHERE Epc IS NOT NULL AND Epc <> ''
    GROUP BY Epc
),
joined AS (
    SELECT
        l.Epc,
        t.EpcAscii,
        t.Time       AS LastTime,
        t.RSSI,
        t.Antenna    AS AntennaPort,
        t.Hostname,
        t.ReaderIP,
        l.Cnt        AS TotalReads,
        r.id         AS ReaderId,
        r.Name       AS ReaderName,
        a.ZoneId     AS ZoneId,
        z.Name       AS ZoneName,
        t.id         AS LastId
    FROM latest l
    JOIN TAG t ON t.id = l.LastId
    LEFT JOIN readers r ON (r.IP = t.ReaderIP OR (r.Hostname IS NOT NULL AND r.Hostname = t.Hostname))
    LEFT JOIN antennas a ON (a.ReaderId = r.id AND a.Port = t.Antenna)
    LEFT JOIN zones z ON (z.id = a.ZoneId)
)";

        // 🇸🇮 Dinamični WHERE
        var where = new StringBuilder();
        var parms = new List<(string name, object? value)>();

        void AddLike(string col, string? val, string pname)
        {
            if (string.IsNullOrWhiteSpace(val)) return;
            where.Append(where.Length == 0 ? " WHERE " : " AND ");
            where.Append($"{col} LIKE {pname}");
            parms.Add((pname, $"%{val.Trim()}%"));
        }
        void AddEq(string col, object? val, string pname)
        {
            if (val is null) return;
            where.Append(where.Length == 0 ? " WHERE " : " AND ");
            where.Append($"{col} = {pname}");
            parms.Add((pname, val));
        }
        void AddBetween(string col, DateTime? f, DateTime? t, string p1, string p2)
        {
            if (f is null && t is null) return;
            where.Append(where.Length == 0 ? " WHERE " : " AND ");
            if (f is not null && t is not null)
            {
                where.Append($"{col} BETWEEN {p1} AND {p2}");
                parms.Add((p1, f)); parms.Add((p2, t));
            }
            else if (f is not null)
            {
                where.Append($"{col} >= {p1}");
                parms.Add((p1, f));
            }
            else
            {
                where.Append($"{col} <= {p2}");
                parms.Add((p2, t));
            }
        }
        void AddRangeInt(string col, int? min, int? max, string p1, string p2)
        {
            if (min is null && max is null) return;
            where.Append(where.Length == 0 ? " WHERE " : " AND ");
            if (min is not null && max is not null)
            {
                where.Append($"{col} BETWEEN {p1} AND {p2}");
                parms.Add((p1, min)); parms.Add((p2, max));
            }
            else if (min is not null)
            {
                where.Append($"{col} >= {p1}");
                parms.Add((p1, min));
            }
            else
            {
                where.Append($"{col} <= {p2}");
                parms.Add((p2, max));
            }
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            where.Append(where.Length == 0 ? " WHERE (" : " AND (");
            where.Append(@" j.Epc LIKE @p_q OR j.EpcAscii LIKE @p_q OR j.Hostname LIKE @p_q OR j.ReaderIP LIKE @p_q
                         OR j.ReaderName LIKE @p_q OR j.ZoneName LIKE @p_q )");
            parms.Add(("@p_q", $"%{q!.Trim()}%"));
        }

        AddLike("j.Epc", epc, "@p_epc");
        AddLike("j.EpcAscii", ascii, "@p_ascii");
        AddLike("j.Hostname", hostname, "@p_host");
        AddLike("j.ReaderName", reader, "@p_reader");
        AddLike("j.ZoneName", zone, "@p_zone");
        AddEq("j.AntennaPort", antenna, "@p_ant");
        AddBetween("j.LastTime", from, to, "@p_from", "@p_to");
        AddRangeInt("j.RSSI", rssiMin, rssiMax, "@p_rmin", "@p_rmax");

        var sqlCount = cte + " SELECT COUNT(*) FROM joined j " + where.ToString();

        var sqlPage = cte + @"
SELECT
    j.Epc, j.EpcAscii, j.LastTime, j.RSSI,
    j.AntennaPort, j.Hostname, j.ReaderIP,
    j.TotalReads, j.ReaderName, j.ZoneName
FROM joined j
" + where.ToString() + @"
ORDER BY j.LastTime DESC, j.Epc DESC
LIMIT @p_skip, @p_take;";

        await using var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();

        // 🇸🇮 total
        await using (var cmdCount = conn.CreateCommand())
        {
            cmdCount.CommandText = sqlCount;
            foreach (var (n, v) in parms)
            {
                var p = cmdCount.CreateParameter(); p.ParameterName = n; p.Value = v ?? DBNull.Value; cmdCount.Parameters.Add(p);
            }
            var total = Convert.ToInt32(await cmdCount.ExecuteScalarAsync() ?? 0);

            // 🇸🇮 page
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sqlPage;
            var pSkip = cmd.CreateParameter(); pSkip.ParameterName = "@p_skip"; pSkip.Value = (page - 1) * pageSize; cmd.Parameters.Add(pSkip);
            var pTake = cmd.CreateParameter(); pTake.ParameterName = "@p_take"; pTake.Value = pageSize; cmd.Parameters.Add(pTake);
            foreach (var (n, v) in parms)
            {
                var p = cmd.CreateParameter(); p.ParameterName = n; p.Value = v ?? DBNull.Value; cmd.Parameters.Add(p);
            }

            var items = new List<object>(pageSize);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                items.Add(new
                {
                    epc = rdr["Epc"] as string,
                    epcAscii = rdr["EpcAscii"] as string,
                    lastTime = rdr["LastTime"] is DBNull ? null : (DateTime?)rdr["LastTime"],
                    rssi = rdr["RSSI"] is DBNull ? null : (int?)rdr["RSSI"],
                    antenna = rdr["AntennaPort"] is DBNull ? null : (int?)rdr["AntennaPort"],
                    hostname = rdr["Hostname"] as string,
                    readerIp = rdr["ReaderIP"] as string,
                    totalReads = rdr["TotalReads"] is DBNull ? 0 : Convert.ToInt32(rdr["TotalReads"]),
                    reader = rdr["ReaderName"] as string,
                    zone = rdr["ZoneName"] as string
                });
            }

            return Ok(new { page, pageSize, total, items });
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 🇸🇮 Desni panel – "rich" zgodovina (reader + cona za izbran EPC)
    [HttpGet("by-epc/details/{epc}"), Authorize]
    public async Task<IActionResult> ByEpcDetails([FromRoute] string epc, [FromQuery] int take = 200)
    {
        if (!await HasPermAsync("tags.view")) return Forbidden("tags.view");
        if (string.IsNullOrWhiteSpace(epc))
            return BadRequest(new { error = "EPC je obvezen." });

        take = Math.Clamp(take, 1, 1000);

        var sql = @"
SELECT
  t.id, t.Epc, t.EpcAscii, t.Time, t.RSSI, t.Antenna, t.Hostname, t.ReaderIP,
  r.Name  AS ReaderName,
  z.Name  AS ZoneName
FROM TAG t
LEFT JOIN readers  r ON (r.IP = t.ReaderIP OR (r.Hostname IS NOT NULL AND r.Hostname = t.Hostname))
LEFT JOIN antennas a ON (a.ReaderId = r.id AND a.Port = t.Antenna)
LEFT JOIN zones    z ON (z.id = a.ZoneId)
WHERE t.Epc = @p_epc
ORDER BY t.id DESC
LIMIT @p_take;";

        await using var conn = _db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var p1 = cmd.CreateParameter(); p1.ParameterName = "@p_epc"; p1.Value = epc; cmd.Parameters.Add(p1);
        var p2 = cmd.CreateParameter(); p2.ParameterName = "@p_take"; p2.Value = take; cmd.Parameters.Add(p2);

        var items = new List<object>(take);
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            items.Add(new
            {
                id = rdr["id"],
                epc = rdr["Epc"] as string,
                epcAscii = rdr["EpcAscii"] as string,
                time = rdr["Time"] is DBNull ? null : (DateTime?)rdr["Time"],
                rssi = rdr["RSSI"] is DBNull ? null : (int?)rdr["RSSI"],
                antenna = rdr["Antenna"] is DBNull ? null : (int?)rdr["Antenna"],
                hostname = rdr["Hostname"] as string,
                readerIp = rdr["ReaderIP"] as string,
                reader = rdr["ReaderName"] as string,
                zone = rdr["ZoneName"] as string
            });
        }

        return Ok(items);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 🇸🇮 INGEST – Zebra FX webhook (SIMPLE)
    //
    // Sprejme:
    //  - posamezen objekt ali JArray objektov:
    //    [
    //      { "data": { "idHex":"...", "antenna":1, "peakRssi":-72, "reads":1, "hostName":"FX7500..." }, "timestamp":"2025-10-23T08:06:47.198+0000", "type":"SIMPLE" }
    //    ]
    //
    // Zapiše v TAG: Epc, EpcAscii, Time, Antenna, RSSI, SEEN_COUNT, Hostname, ReaderIP
    //
    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] JsonElement payload)
    {
        // 🇸🇮 1) Pripravimo seznam itemov (lahko array ali single)
        var items = new List<JsonElement>();
        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in payload.EnumerateArray())
                items.Add(it);
        }
        else if (payload.ValueKind == JsonValueKind.Object)
        {
            items.Add(payload);
        }
        else
        {
            return BadRequest(new { error = "Pričakovan je JSON objekt ali array objektov." });
        }

        // 🇸🇮 2) Določi IP pošiljatelja (proxy-ji naj se upoštevajo)
        string? remoteIp = null;
        // X-Forwarded-For lahko vsebuje več IP-jev, vzamemo prvega
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var fwd) && fwd.Count > 0)
        {
            var raw = fwd.ToString();
            var first = raw.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(first))
                remoteIp = first;
        }
        // fallback: neposredni naslov
        if (string.IsNullOrEmpty(remoteIp))
            remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        int saved = 0;
        // Zberemo podatke za presence processing po SaveChanges
        var presenceCandidates = new List<(string Epc, string? Ip, string? Host, int? Port)>();

        foreach (var it in items)
        {
            try
            {
                // struktura: { "data": { ... }, "timestamp": "...", ... }
                if (!it.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    continue;

                // 🇸🇮 EPC (idHex)
                string? epc = data.TryGetProperty("idHex", out var vId) && vId.ValueKind == JsonValueKind.String
                    ? vId.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(epc))
                    continue; // brez EPC ne zapisujemo

                // 🇸🇮 Čas dogodka
                DateTime time;
                string? ts = it.TryGetProperty("timestamp", out var vTs) && vTs.ValueKind == JsonValueKind.String ? vTs.GetString() : null;
                if (!TryParseZebraTimestamp(ts, out time))
                    time = DateTime.UtcNow; // fallback, če reader pošlje čuden format

                // 🇸🇮 Antena (port)
                int? antenna = null;
                if (data.TryGetProperty("antenna", out var vAnt))
                {
                    if (vAnt.ValueKind == JsonValueKind.Number && vAnt.TryGetInt32(out var aNum))
                        antenna = aNum;
                    else if (vAnt.ValueKind == JsonValueKind.String && int.TryParse(vAnt.GetString(), out var aStr))
                        antenna = aStr;
                }

                // 🇸🇮 RSSI
                int? rssi = null;
                if (data.TryGetProperty("peakRssi", out var vRssi))
                {
                    if (vRssi.ValueKind == JsonValueKind.Number)
                    {
                        // lahko je int ali double
                        if (vRssi.TryGetInt32(out var rInt)) rssi = rInt;
                        else if (vRssi.TryGetDouble(out var rDbl)) rssi = (int)Math.Round(rDbl);
                    }
                    else if (vRssi.ValueKind == JsonValueKind.String && int.TryParse(vRssi.GetString(), out var rStr))
                    {
                        rssi = rStr;
                    }
                }

                // 🇸🇮 Število branj (reads)
                int? seen = null;
                if (data.TryGetProperty("reads", out var vReads))
                {
                    if (vReads.ValueKind == JsonValueKind.Number && vReads.TryGetInt32(out var cInt))
                        seen = cInt;
                    else if (vReads.ValueKind == JsonValueKind.String && int.TryParse(vReads.GetString(), out var cStr))
                        seen = cStr;
                }

                // 🇸🇮 Hostname
                string? host = data.TryGetProperty("hostName", out var vHost) && vHost.ValueKind == JsonValueKind.String
                    ? vHost.GetString()
                    : null;

                // 🇸🇮 EpcAscii (če ga reader ne pošlje posebej, ga izračunamo)
                string epcAscii = HexToAscii(epc);

                // 🇸🇮 3) Zapišemo v bazo
                _db.TAG.Add(new Signalko.Core.Tag
                {
                    Epc = epc,
                    EpcAscii = string.IsNullOrEmpty(epcAscii) ? null : epcAscii,
                    Time = time,
                    Antenna = antenna,
                    RSSI = rssi,
                    SEEN_COUNT = seen,
                    Hostname = host,
                    ReaderIP = remoteIp
                });

                presenceCandidates.Add((epc, remoteIp, host, antenna));
                saved++;
            }
            catch
            {
                // 🇸🇮 eno slabo sporočilo ne sme ustaviti cele serije
                continue;
            }
        }

        if (saved > 0)
        {
            await _db.SaveChangesAsync();

            // Prisotnostna logika (v ozadju — napake ne smejo prekiniti odgovora)
            foreach (var (epc, ip, host, port) in presenceCandidates)
            {
                try { await _presence.ProcessTagAsync(epc, ip, host, port); }
                catch { /* ne blokira */ }
            }
        }

        return Ok(new { accepted = items.Count, saved });
    }
}
