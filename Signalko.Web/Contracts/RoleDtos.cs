namespace Signalko.Web.Contracts;

// DTO za branje role
public record RoleDto(
    int id,
    string Name
);

// DTO za ustvarjanje nove role
public record RoleCreateDto(
    string Name
);
