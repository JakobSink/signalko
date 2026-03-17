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

    /// <summary>null = system role (visible to all tenants); set = custom role scoped to this license</summary>
    public int? LicenseId { get; set; }

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

// ============ MODULES ============
[Table("modules")]
public class Module
{
    [Key] public int id { get; set; }
    [Required, MaxLength(50)]  public string  Code        { get; set; } = "";
    [Required, MaxLength(100)] public string  Name        { get; set; } = "";
    [MaxLength(500)]           public string? Description { get; set; }
    [MaxLength(100)]           public string? Icon        { get; set; }
    public bool IsCore { get; set; } = false;  // core modules can't be disabled
}

[Table("license_modules")]
public class LicenseModule
{
    [Key] public int id { get; set; }
    public int LicenseId { get; set; }
    [Required, MaxLength(50)] public string ModuleCode { get; set; } = "";
    public DateTime EnabledAt { get; set; }
    public int? EnabledByUserId { get; set; }

    [ForeignKey(nameof(LicenseId))]      public License? License   { get; set; }
    [ForeignKey(nameof(EnabledByUserId))] public User?    EnabledBy { get; set; }
}

// ============ LAUNDRY ============
/// <summary>Statuses stored in laundry_item_events.ToStatus (workflow steps)</summary>
public static class LaundryStatus
{
    public const string Deposited       = "deposited";
    public const string InWash          = "in_wash";
    public const string Sorting         = "sorting";
    public const string Ironing         = "ironing";
    public const string Damaged         = "damaged";
    public const string Sewing          = "sewing";
    public const string Repaired        = "repaired";
    public const string WriteOffProposed = "write_off_proposed";
    public const string WrittenOff      = "written_off";
    public const string InSet           = "in_set";
    public const string Ready           = "ready";
    public const string PickedUp        = "picked_up";
}

[Table("laundry_items")]
public class LaundryItem
{
    [Key] public int id { get; set; }
    public int LicenseId { get; set; }
    public int? OwnerId { get; set; }
    [Required, MaxLength(200)] public string  Name     { get; set; } = "";
    [MaxLength(50)]            public string? Category { get; set; }
    public int?    TagId  { get; set; }
    [MaxLength(50)] public string Status { get; set; } = "active"; // active | written_off
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(OwnerId))] public User? Owner { get; set; }
    [ForeignKey(nameof(TagId))]   public Tag?  Tag   { get; set; }
    [JsonIgnore] public ICollection<LaundryItemEvent> Events { get; set; } = new List<LaundryItemEvent>();
}

[Table("laundry_bins")]
public class LaundryBin
{
    [Key] public int id { get; set; }
    public int LicenseId { get; set; }
    [Required, MaxLength(100)] public string Label  { get; set; } = "";
    [MaxLength(30)]            public string Status { get; set; } = "open"; // open | in_wash | done
    public DateTime  OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int? OpenedByUserId { get; set; }

    [ForeignKey(nameof(OpenedByUserId))] public User? OpenedBy { get; set; }
    [JsonIgnore] public ICollection<LaundryBinItem> Items { get; set; } = new List<LaundryBinItem>();
}

[Table("laundry_bin_items")]
public class LaundryBinItem
{
    [Key] public int id { get; set; }
    public int BinId  { get; set; }
    public int ItemId { get; set; }
    public DateTime ScannedAt { get; set; }
    public int? ScannedByUserId { get; set; }

    [ForeignKey(nameof(BinId))]           public LaundryBin?  Bin       { get; set; }
    [ForeignKey(nameof(ItemId))]          public LaundryItem? Item      { get; set; }
    [ForeignKey(nameof(ScannedByUserId))] public User?        ScannedBy { get; set; }
}

[Table("laundry_item_events")]
public class LaundryItemEvent
{
    [Key] public int id { get; set; }
    public int  ItemId   { get; set; }
    public int? WorkerId { get; set; }
    [MaxLength(50)]  public string? FromStatus { get; set; }
    [Required, MaxLength(50)] public string ToStatus { get; set; } = "";
    [MaxLength(1000)] public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }

    [ForeignKey(nameof(ItemId))]   public LaundryItem? Item   { get; set; }
    [ForeignKey(nameof(WorkerId))] public User?        Worker { get; set; }
}

[Table("laundry_sets")]
public class LaundrySet
{
    [Key] public int id { get; set; }
    public int LicenseId { get; set; }
    public int OwnerId   { get; set; }
    public int? AssembledByUserId { get; set; }
    public DateTime  AssembledAt { get; set; }
    public DateTime? PickedUpAt  { get; set; }
    public int? PickedUpByUserId { get; set; }
    [MaxLength(30)] public string Status { get; set; } = "ready"; // ready | picked_up

    [ForeignKey(nameof(OwnerId))]            public User? Owner      { get; set; }
    [ForeignKey(nameof(AssembledByUserId))]  public User? AssembledBy { get; set; }
    [ForeignKey(nameof(PickedUpByUserId))]   public User? PickedUpBy  { get; set; }
    [JsonIgnore] public ICollection<LaundrySetItem> Items { get; set; } = new List<LaundrySetItem>();
}

[Table("laundry_set_items")]
public class LaundrySetItem
{
    [Key] public int id { get; set; }
    public int SetId  { get; set; }
    public int ItemId { get; set; }

    [ForeignKey(nameof(SetId))]  public LaundrySet?  Set  { get; set; }
    [ForeignKey(nameof(ItemId))] public LaundryItem? Item { get; set; }
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
    public DateTime  CreatedAt   { get; set; }
    public DateTime  UpdatedAt   { get; set; }
    public DateTime? ActivatedAt { get; set; }  // set on first user signup
}

// ============ SUPERADMIN USER ============
[Table("superadmin_users")]
public class SuperAdminUser
{
    [Key] public int id { get; set; }
    [Required, MaxLength(255)] public string Email { get; set; } = "";
    [Required] public string PasswordHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
