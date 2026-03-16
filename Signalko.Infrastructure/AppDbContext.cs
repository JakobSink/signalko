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
