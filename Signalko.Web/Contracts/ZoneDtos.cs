namespace Signalko.Web.Contracts;

// osnovna cona (za CRUD in dropdown-e)
public record ZoneDto(int id, string? Name, string? Type);

// antena + reader + role, kot jo prikažemo v UI
public record ZoneAntennaDto(
    int AntennaId,
    int Port,
    int ReaderId,
    string? ReaderName,
    string? ReaderIP,
    bool ReaderEnabled,
    int RoleId,
    string? RoleName
);

// ena kartica (cona) z antenami
public record ZoneWithAntennasDto(
    int id,
    string? Name,
    string? Type,
    List<ZoneAntennaDto> Antennas
);

// za premik antene med conami
public record AssignAntennaZoneDto(
    int AntennaId,
    int ZoneId   // 0 = "Ni dodeljeno"
);
