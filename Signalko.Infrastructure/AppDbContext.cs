using Microsoft.EntityFrameworkCore;
using Signalko.Core;

namespace Signalko.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Validation>  Validation   => Set<Validation>();
    public DbSet<UserRole>    Roles        => Set<UserRole>();
    public DbSet<AntennaRole> Role         => Set<AntennaRole>();

    public DbSet<User>        users        => Set<User>();
    public DbSet<Reader>      readers      => Set<Reader>();
    public DbSet<Antenna>     antennas     => Set<Antenna>();
    public DbSet<Zone>        zones        => Set<Zone>();

    public DbSet<Author>      Author       => Set<Author>();
    public DbSet<Asset>       ASSET        => Set<Asset>();
    public DbSet<AssetLoan>   assets_loans => Set<AssetLoan>();

    public DbSet<Tag>             TAG              => Set<Tag>();
    public DbSet<ExchangeRequest> ExchangeRequests => Set<ExchangeRequest>();
    public DbSet<UserPresence>    UserPresences    => Set<UserPresence>();
    public DbSet<Permission>     Permissions     => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<License>        Licenses        => Set<License>();
    public DbSet<Module>         Modules         => Set<Module>();
    public DbSet<LicenseModule>  LicenseModules  => Set<LicenseModule>();

    // Laundry
    public DbSet<LaundryItem>      LaundryItems      => Set<LaundryItem>();
    public DbSet<LaundryBin>       LaundryBins       => Set<LaundryBin>();
    public DbSet<LaundryBinItem>   LaundryBinItems   => Set<LaundryBinItem>();
    public DbSet<LaundryItemEvent> LaundryItemEvents => Set<LaundryItemEvent>();
    public DbSet<LaundrySet>       LaundrySets       => Set<LaundrySet>();
    public DbSet<LaundrySetItem>   LaundrySetItems   => Set<LaundrySetItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>().HasIndex(x => x.CardID).IsUnique();
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();

        b.Entity<Antenna>()
            .HasOne(a => a.Reader)
            .WithMany(r => r.Antennas)
            .HasForeignKey(a => a.ReaderId);

        b.Entity<Antenna>()
            .HasOne(a => a.Zone)
            .WithMany(z => z.Antennas)
            .HasForeignKey(a => a.ZoneId);

        b.Entity<Antenna>()
            .HasOne(a => a.Role)
            .WithMany(r => r.Antennas)
            .HasForeignKey(a => a.RoleID);

        b.Entity<Reader>(e =>
        {
            e.Property(x => x.IP).HasMaxLength(45);
            e.Property(x => x.Hostname).HasMaxLength(100);
        });

        b.Entity<Tag>(e =>
        {
            e.Property(x => x.ReaderIP).HasMaxLength(45);
            e.Property(x => x.Hostname).HasMaxLength(100);
        });

        // FIX: LoanedAt / ReturnedAt are now DateTime (datetime in MySQL)
        b.Entity<AssetLoan>(e =>
        {
            e.Property(x => x.LoanedAt).HasColumnType("datetime");
            e.Property(x => x.ReturnedAt).HasColumnType("datetime");
        });

        b.Entity<ExchangeRequest>(e =>
        {
            e.Property(x => x.CreatedAt).HasColumnType("datetime");
            e.Property(x => x.RespondedAt).HasColumnType("datetime");
            e.HasOne(x => x.FromUser).WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ToUser).WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserPresence>(e =>
        {
            e.Property(x => x.ScannedAt).HasColumnType("datetime");
            e.HasOne(x => x.User)    .WithMany().HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Zone)    .WithMany().HasForeignKey(x => x.ZoneId)   .IsRequired(false);
            e.HasOne(x => x.Antenna) .WithMany().HasForeignKey(x => x.AntennaId).IsRequired(false);
            e.HasIndex(x => new { x.UserId, x.ScannedAt });
        });

        b.Entity<Asset>(e =>
        {
            e.HasOne(x => x.Author)
             .WithMany(a => a.Assets)
             .HasForeignKey(x => x.AuthorId)
             .IsRequired(false);

            e.HasOne(x => x.Tag)
             .WithMany()
             .HasForeignKey(x => x.TagId)
             .IsRequired(false);
        });

        b.Entity<LicenseModule>(e =>
        {
            e.HasIndex(x => new { x.LicenseId, x.ModuleCode }).IsUnique();
            e.HasOne(x => x.License)  .WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.EnabledBy).WithMany().HasForeignKey(x => x.EnabledByUserId).IsRequired(false);
        });

        b.Entity<LaundryItem>(e =>
        {
            e.Property(x => x.CreatedAt).HasColumnType("datetime");
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).IsRequired(false);
            e.HasOne(x => x.Tag)  .WithMany().HasForeignKey(x => x.TagId)  .IsRequired(false);
        });

        b.Entity<LaundryBin>(e =>
        {
            e.Property(x => x.OpenedAt).HasColumnType("datetime");
            e.Property(x => x.ClosedAt).HasColumnType("datetime");
            e.HasOne(x => x.OpenedBy).WithMany().HasForeignKey(x => x.OpenedByUserId).IsRequired(false);
        });

        b.Entity<LaundryBinItem>(e =>
        {
            e.Property(x => x.ScannedAt).HasColumnType("datetime");
            e.HasOne(x => x.Bin)      .WithMany(b => b.Items).HasForeignKey(x => x.BinId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Item)     .WithMany()             .HasForeignKey(x => x.ItemId);
            e.HasOne(x => x.ScannedBy).WithMany()             .HasForeignKey(x => x.ScannedByUserId).IsRequired(false);
        });

        b.Entity<LaundryItemEvent>(e =>
        {
            e.Property(x => x.CreatedAt).HasColumnType("datetime");
            e.HasOne(x => x.Item)  .WithMany(i => i.Events).HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Worker).WithMany()              .HasForeignKey(x => x.WorkerId).IsRequired(false);
        });

        b.Entity<LaundrySet>(e =>
        {
            e.Property(x => x.AssembledAt).HasColumnType("datetime");
            e.Property(x => x.PickedUpAt) .HasColumnType("datetime");
            e.HasOne(x => x.Owner)      .WithMany().HasForeignKey(x => x.OwnerId);
            e.HasOne(x => x.AssembledBy).WithMany().HasForeignKey(x => x.AssembledByUserId).IsRequired(false);
            e.HasOne(x => x.PickedUpBy) .WithMany().HasForeignKey(x => x.PickedUpByUserId) .IsRequired(false);
        });

        b.Entity<LaundrySetItem>(e =>
        {
            e.HasOne(x => x.Set) .WithMany(s => s.Items).HasForeignKey(x => x.SetId) .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Item).WithMany()             .HasForeignKey(x => x.ItemId);
        });

        b.Entity<RolePermission>(e =>
        {
            e.HasKey(rp => new { rp.RoleId, rp.PermissionId });
            e.HasOne(rp => rp.Role)
             .WithMany(r => r.RolePermissions)
             .HasForeignKey(rp => rp.RoleId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(rp => rp.Permission)
             .WithMany(p => p.RolePermissions)
             .HasForeignKey(rp => rp.PermissionId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
