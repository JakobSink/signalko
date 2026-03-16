namespace Signalko.Web.Contracts;

// ── Loan DTOs ─────────────────────────────────────────────────────────────────

/// <summary>Request body for POST /api/Loan</summary>
public sealed record LoanCreateRequestDto(
    int  UserId,
    int  AssetId,
    int? ZoneId
);

/// <summary>Request body for POST /api/Loan/return</summary>
public sealed record LoanReturnDto(int LoanId);

/// <summary>Full loan response with resolved names</summary>
public sealed record LoanResponseDto(
    int       Id,
    int       AssetId,
    string?   AssetName,
    int       UserId,
    string?   UserName,
    int?      ZoneId,
    string?   ZoneName,
    DateTime? LoanedAt,
    DateTime? ReturnedAt,
    bool      Active
);

/// <summary>Legacy DTO — users last seen on LOAN antenna</summary>
public sealed record LoanUserLastSeenDto(
    int      UserId,
    string   FullName,
    string   CardId,
    DateTime LastSeen,
    int      AntennaId,
    string?  AntennaName,
    int?     ZoneId,
    string?  ZoneName
);

// ── Asset DTOs ────────────────────────────────────────────────────────────────

/// <summary>Create/update asset — supports EPC lookup to link tag</summary>
public record AssetUpsertDto(
    string? Name,
    string? Description,
    string? Epc,
    string? EpcAscii,
    int?    TagId,
    int?    AuthorId,
    string? Icon
);

// keep old name as alias so existing code compiles
public record AssetCreateDto(
    string? Name,
    string? Details,
    int?    TagId,
    int?    AuthorId
);

// ── User DTOs ─────────────────────────────────────────────────────────────────

/// <summary>Safe user response — no password hash</summary>
public sealed record UserSafeDto(
    int     Id,
    string  CardID,
    string  Name,
    string? Surname,
    string  Email
);

/// <summary>Full user info including role — for admin endpoints</summary>
public sealed record UserAdminDto(
    int     Id,
    string  CardID,
    string  Name,
    string? Surname,
    string  Email,
    int?    RoleId,
    string? RoleName,
    string? CardEpc = null,
    bool    IsActive = true,
    string  Language = "sl"
);

public sealed record UserCreateDto(
    string  CardID,
    string  Name,
    string? Surname,
    string? Email,
    string  Password,
    int?    ValidationId,
    int?    RoleId,
    string? CardEpc = null
);

/// <summary>Update DTO — all fields optional; password only hashed if provided</summary>
public sealed record UserUpdateDto(
    string? CardID,
    string? Name,
    string? Surname,
    string? Email,
    string? Password,
    int?    RoleId,
    string? CardEpc = null,
    bool?   IsActive = null,
    string? Language = null
);

// ── Exchange Request DTOs ─────────────────────────────────────────────────────

public sealed record ExchangeCreateDto(
    int     FromUserId,
    int     ToUserId,
    int     AssetId,
    string? Message
);

public sealed record ExchangeRespondDto(bool Accept);

public sealed record ExchangeResponseDto(
    int       Id,
    int       FromUserId,
    string?   FromUserName,
    string?   FromUserCard,
    int       ToUserId,
    string?   ToUserName,
    int       AssetId,
    string?   AssetName,
    string    Status,
    DateTime  CreatedAt,
    DateTime? RespondedAt,
    string?   Message
);

// ── License DTOs ──────────────────────────────────────────────────────────────

public sealed record LicenseDto(
    int      Id,
    string   LicenseKey,
    int      MaxUsers,
    int      ActiveUsers,
    int      TotalUsers,
    int      MaxReadingPoints,
    int      ActiveReadingPoints,
    string?  CompanyName,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
