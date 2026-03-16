namespace Signalko.Web.Contracts;

// DTO za branje antene
public record AntennaDto(
    int Id,
    int ReaderId,
    int Port,
    int ZoneId,
    int RoleID,
    string? RoleName,
    bool IsActive
);

// DTO za ustvarjanje/posodabljanje antene – input iz UI
public record AntennaCreateDto(
    int ReaderId,
    int Port,
    int ZoneId,
    int RoleID
);
