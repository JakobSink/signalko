using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Signalko.Core;

// ============ LOOKUPS ============
[Table("Validation")]
public class Validation
{
    [Key] public int id { get; set; }
    public string? Type { get; set; }
}

[Table("Roles")]
public class UserRole
{
    [Key] public int id { get; set; }
    public string? Name { get; set; }

    [JsonIgnore]
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

[Table("Role")]
public class AntennaRole
{
    [Key] public int id { get; set; }

    [Column("name")]
    public string? Name { get; set; }

    [JsonIgnore]
    public ICollection<Antenna> Antennas { get; set; } = new List<Antenna>();
}

[Table("zones")]
public class Zone
{
    [Key] public int id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }

    public int? LicenseId { get; set; }

    [JsonIgnore]
    public ICollection<Antenna> Antennas { get; set; } = new List<Antenna>();
}

// ============ USERS ============
[Table("users")]
public class User
{
    [Key]
    public int id { get; set; }

    [Required]
    [MaxLength(6)]
    public string CardID { get; set; } = "";

    [Required]
    public string Name { get; set; } = "";

    public string? Surname { get; set; }

    [Required]
    [MaxLength(190)]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    public int? ValidationId { get; set; }
    public int? RoleId { get; set; }

    [ForeignKey(nameof(ValidationId))]
    public Validation? Validation { get; set; }

    [ForeignKey(nameof(RoleId))]
    public UserRole? Role { get; set; }

    /// <summary>Hex EPC fizične RFID kartice uporabnika (za prisotnostno sledenje)</summary>
    [MaxLength(128)]
    public string? CardEpc { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(5)]
    public string Language { get; set; } = "sl";

    public int? LicenseId { get; set; }

    [JsonIgnore]
    public ICollection<AssetLoan> Loans { get; set; } = new List<AssetLoan>();
}

// ============ NAPRAVE ============
[Table("readers")]
public class Reader
{
    [Key] public int id { get; set; }
    public string? Name { get; set; }

    [Column(TypeName = "varchar(45)")]
    public string? IP { get; set; }

    public bool Enabled { get; set; } = true;

    [MaxLength(100)]
    public string? Hostname { get; set; }

    public int? LicenseId { get; set; }

    [JsonIgnore]
    public ICollection<Antenna> Antennas { get; set; } = new List<Antenna>();
}

[Table("antennas")]
public class Antenna
{
    [Key] public int id { get; set; }

    public int ReaderId { get; set; }
    public int Port { get; set; }
    public int ZoneId { get; set; }

    [Column("RoleID")]
    public int RoleID { get; set; }

    public bool IsActive { get; set; } = true;

    [JsonIgnore, ForeignKey(nameof(ReaderId))]
    public Reader? Reader { get; set; }

    [JsonIgnore, ForeignKey(nameof(ZoneId))]
    public Zone? Zone { get; set; }

    [JsonIgnore, ForeignKey(nameof(RoleID))]
    public AntennaRole? Role { get; set; }
}

// ============ ASSET ============
[Table("Author")]
public class Author
{
    [Key] public int id { get; set; }
    public string? Name { get; set; }
    public string? Surname { get; set; }

    [JsonIgnore]
    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
}

[Table("ASSET")]
public class Asset
{
    [Key] public int id { get; set; }
    public string? Name { get; set; }
    public string? Details { get; set; }

    [MaxLength(512)]
    public string? Icon { get; set; }   // emoji character OR /assets-img/{file}

    public int? TagId { get; set; }
    public int? AuthorId { get; set; }

    public int? LicenseId { get; set; }

    [ForeignKey(nameof(AuthorId))] public Author? Author { get; set; }
    [ForeignKey(nameof(TagId))]    public Tag? Tag { get; set; }

    [JsonIgnore]
    public ICollection<AssetLoan> Loans { get; set; } = new List<AssetLoan>();
}

// ============ IZPOSOJE ============
[Table("assets_loans")]
public class AssetLoan
{
    [Key] public int id { get; set; }
    public int AssetId { get; set; }
    public int UserId { get; set; }

    // FIX: was TimeOnly — now DateTime so we track both date AND time
    public DateTime? LoanedAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    public int? ZoneId { get; set; }

    [ForeignKey(nameof(AssetId))] public Asset? Asset { get; set; }
    [ForeignKey(nameof(UserId))]  public User? User { get; set; }
    [ForeignKey(nameof(ZoneId))]  public Zone? Zone { get; set; }
}

// ============ EXCHANGE REQUESTS ============
[Table("exchange_requests")]
public class ExchangeRequest
{
    [Key]                    public int      id          { get; set; }
    [Column("from_user_id")] public int      FromUserId  { get; set; }
    [Column("to_user_id")]   public int      ToUserId    { get; set; }
    [Column("asset_id")]     public int      AssetId     { get; set; }
    [Column("Status"),  MaxLength(20)]  public string  Status      { get; set; } = "pending";
    [Column("Message"), MaxLength(500)] public string? Message     { get; set; }
    [Column("created_at")]   public DateTime  CreatedAt   { get; set; }
    [Column("responded_at")] public DateTime? RespondedAt { get; set; }

    [ForeignKey("FromUserId")] public User?  FromUser { get; set; }
    [ForeignKey("ToUserId")]   public User?  ToUser   { get; set; }
    [ForeignKey("AssetId")]    public Asset? Asset    { get; set; }
}

// ============ PRISOTNOST ============
[Table("user_presence")]
public class UserPresence
{
    [Key] public int id { get; set; }

    public int UserId { get; set; }

    /// <summary>"IN" ali "OUT"</summary>
    [MaxLength(10)]
    public string Type { get; set; } = "";

    public DateTime ScannedAt { get; set; }

    public int? ZoneId    { get; set; }
    public int? AntennaId { get; set; }

    [ForeignKey(nameof(UserId))]    public User?    User    { get; set; }
    [ForeignKey(nameof(ZoneId))]    public Zone?    Zone    { get; set; }
    [ForeignKey(nameof(AntennaId))] public Antenna? Antenna { get; set; }
}

// ============ PRAVICE (PERMISSIONS) ============
[Table("permissions")]
public class Permission
{
    [Key] public int id { get; set; }
    [Required, MaxLength(100)] public string Code { get; set; } = "";
    [MaxLength(200)] public string? Label { get; set; }
    [MaxLength(100)] public string? Category { get; set; }

    [JsonIgnore]
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

[Table("role_permissions")]
public class RolePermission
{
    public int RoleId { get; set; }
    public int PermissionId { get; set; }

    [ForeignKey(nameof(RoleId))]       public UserRole?   Role       { get; set; }
    [ForeignKey(nameof(PermissionId))] public Permission? Permission { get; set; }
}

// ============ TAG ============
[Table("TAG")]
public class Tag
{
    [Key] public int id { get; set; }

    public string? Epc { get; set; }
    public DateTime? Time { get; set; }

    public int? Antenna { get; set; }
    public int? RSSI { get; set; }
    public int? SEEN_COUNT { get; set; }

    [Column(TypeName = "varchar(45)")]
    public string? ReaderIP { get; set; }

    [MaxLength(100)]
    public string? Hostname { get; set; }

    [MaxLength(128)]
    public string? EpcAscii { get; set; }

    public int? LicenseId { get; set; }
}

// ============ LICENSE ============
[Table("licenses")]
public class License
{
    [Key] public int id { get; set; }
    [Required, MaxLength(30)] public string LicenseKey { get; set; } = "";
    public int MaxUsers { get; set; } = 10;
    public int MaxReadingPoints { get; set; } = 5;
    [MaxLength(255)] public string? CompanyName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
