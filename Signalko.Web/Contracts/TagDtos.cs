namespace Signalko.Web.Contracts;

// DTO za vnos tag-a (single ali bulk)
public record TagCreateDto(
    string? Epc,
    DateTime? Time,
    int? Antenna,
    int? RSSI,
    int? SEEN_COUNT,
    string? ReaderIP,
    string? Hostname
);
