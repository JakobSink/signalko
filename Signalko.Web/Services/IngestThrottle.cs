using System.Collections.Concurrent;

namespace Signalko.Web.Services;

// 🇸🇮 V spomin si držimo zadnji čas in števec za kombinacijo (EPC + ReaderIP)
public sealed class IngestThrottle
{
    private readonly ConcurrentDictionary<string, Entry> _last = new();

    private static string Key(string epc, string readerIp)
        => $"{epc}@@{readerIp}";

    public bool ShouldStore(string epc, string readerIp, DateTime nowUtc, TimeSpan minGap, out int seenBump)
    {
        seenBump = 1;
        var key = Key(epc, readerIp);
        var e = _last.GetOrAdd(key, _ => new Entry(nowUtc));

        // če smo znotraj okna -> ne zapiši, samo dvigni lokalni števec
        var diff = nowUtc - e.LastUtc;
        if (diff < minGap)
        {
            e.Increment();
            seenBump = e.SeenInGap;
            return false;
        }

        // izven okna -> resetiraj in dovoli zapis
        e.Reset(nowUtc);
        seenBump = e.SeenInGap;
        return true;
    }

    private sealed class Entry
    {
        public DateTime LastUtc { get; private set; }
        public int SeenInGap { get; private set; }

        public Entry(DateTime initial)
        {
            LastUtc = initial;
            SeenInGap = 1;
        }

        public void Increment() => SeenInGap++;

        public void Reset(DateTime now)
        {
            LastUtc = now;
            SeenInGap = 1;
        }
    }
}
