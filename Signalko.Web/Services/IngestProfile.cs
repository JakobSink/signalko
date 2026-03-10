namespace Signalko.Web.Services;

// 🇸🇮 Profili – povejo, kakšen min. razmik med zapisi uporabljamo
public enum IngestProfile
{
    Normal,
    Inventory,
    Loans
}

public sealed class IngestProfileState
{
    // 🇸🇮 thread-safe, ker ga bomo menjali z API
    private readonly object _lock = new();
    private IngestProfile _current = IngestProfile.Normal;

    public IngestProfile Current
    {
        get { lock (_lock) return _current; }
        set { lock (_lock) _current = value; }
    }

    public TimeSpan MinGapFor(IngestProfile p) => p switch
    {
        IngestProfile.Normal => TimeSpan.FromSeconds(10), //️⃣ varčno z bazo
        IngestProfile.Inventory => TimeSpan.FromSeconds(2),
        IngestProfile.Loans => TimeSpan.FromSeconds(1),  //️⃣ agresivno skeniranje
        _ => TimeSpan.FromSeconds(10)
    };

    public TimeSpan CurrentMinGap => MinGapFor(Current);
}
