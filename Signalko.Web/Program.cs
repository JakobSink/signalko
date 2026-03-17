using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Controllers;
using Signalko.Web.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not set.");
builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseMySql(cs, new MySqlServerVersion(new Version(9, 4, 0)),
        my => my.MigrationsAssembly("Signalko.Infrastructure"));
});

builder.Services.AddScoped<JwtTokenService>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? "REPLACE_WITH_STRONG_SECRET_MIN_32_CHARS_DEFAULT";
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer           = false,
            ValidateAudience         = false,
            // Map JWT "role" claim directly so User.IsInRole("Admin") works
            RoleClaimType            = "role",
            NameClaimType            = "sub",
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddSingleton<Signalko.Web.Services.IngestThrottle>();
builder.Services.AddSingleton<Signalko.Web.Services.IngestProfileState>();
builder.Services.AddScoped<Signalko.Web.Services.PresenceService>();

builder.Services.AddCors(o => o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// ── Deleted-user guard: return 401 if authenticated user no longer exists in DB ──
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var userIdStr = context.User.Claims
            .FirstOrDefault(c => c.Type == "sub" ||
                                 c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdStr, out var userId))
        {
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            var exists = await db.users.AsNoTracking().AnyAsync(u => u.id == userId);
            if (!exists)
            {
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\":\"Seja ni več veljavna.\"}");
                return;
            }
        }
    }
    await next();
});

app.UseStaticFiles();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTime.UtcNow }));

app.MapFallback(async ctx =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

// ── Migrate + seed roles + admin synchronously ────────────────────────────────
Console.WriteLine("[DB] Running migrations + core seed...");
await MigrateAndSeedCoreAsync(app);
Console.WriteLine("[DB] Core seed done.");

// ── Test presence data in background (non-critical) ───────────────────────────
_ = Task.Run(async () =>
{
    await Task.Delay(1000);
    await SeedTestDataAsync(app);
});

app.Run();

// ── Migrate + create extra tables + seed roles + admin ────────────────────────
static async Task MigrateAndSeedCoreAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
        Console.WriteLine("[DB] MigrateAsync complete.");

        // Extra tables not in EF migrations
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS exchange_requests (
                id            INT AUTO_INCREMENT PRIMARY KEY,
                from_user_id  INT          NOT NULL,
                to_user_id    INT          NOT NULL,
                asset_id      INT          NOT NULL,
                Status        VARCHAR(20)  NOT NULL DEFAULT 'pending',
                Message       VARCHAR(500) NULL,
                created_at    DATETIME     NOT NULL,
                responded_at  DATETIME     NULL,
                INDEX idx_ex_to   (to_user_id),
                INDEX idx_ex_from (from_user_id),
                CONSTRAINT fk_ex_from  FOREIGN KEY (from_user_id) REFERENCES users(id)  ON DELETE CASCADE,
                CONSTRAINT fk_ex_to    FOREIGN KEY (to_user_id)   REFERENCES users(id)  ON DELETE CASCADE,
                CONSTRAINT fk_ex_asset FOREIGN KEY (asset_id)     REFERENCES ASSET(id)  ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");

        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `user_presence` (
                `id`         INT AUTO_INCREMENT PRIMARY KEY,
                `UserId`     INT NOT NULL,
                `Type`       VARCHAR(10) NOT NULL,
                `ScannedAt`  DATETIME NOT NULL,
                `ZoneId`     INT NULL,
                `AntennaId`  INT NULL,
                INDEX `idx_up_user_time` (`UserId`, `ScannedAt`),
                CONSTRAINT `fk_up_user`    FOREIGN KEY (`UserId`)    REFERENCES `users`(`id`)    ON DELETE CASCADE,
                CONSTRAINT `fk_up_zone`    FOREIGN KEY (`ZoneId`)    REFERENCES `zones`(`id`)    ON DELETE SET NULL,
                CONSTRAINT `fk_up_antenna` FOREIGN KEY (`AntennaId`) REFERENCES `antennas`(`id`) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");

        // RBAC tables — belt-and-suspenders in case EF migration didn't run
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `permissions` (
                `id`       INT AUTO_INCREMENT PRIMARY KEY,
                `Code`     VARCHAR(100) NOT NULL,
                `Label`    VARCHAR(200) NOT NULL,
                `Category` VARCHAR(100) NOT NULL,
                UNIQUE INDEX `uix_perm_code` (`Code`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `role_permissions` (
                `RoleId`       INT NOT NULL,
                `PermissionId` INT NOT NULL,
                PRIMARY KEY (`RoleId`, `PermissionId`),
                CONSTRAINT `fk_rp_role` FOREIGN KEY (`RoleId`)       REFERENCES `Roles`(`id`)       ON DELETE CASCADE,
                CONSTRAINT `fk_rp_perm` FOREIGN KEY (`PermissionId`) REFERENCES `permissions`(`id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        Console.WriteLine("[DB] RBAC tables ensured.");

        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `licenses` (
                `id`          INT AUTO_INCREMENT PRIMARY KEY,
                `LicenseKey`  VARCHAR(30)  NOT NULL,
                `MaxUsers`    INT          NOT NULL DEFAULT 10,
                `CompanyName` VARCHAR(255) NULL,
                `CreatedAt`   DATETIME     NOT NULL,
                `UpdatedAt`   DATETIME     NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");

        // Add MaxReadingPoints to licenses
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `licenses` ADD COLUMN `MaxReadingPoints` INT NOT NULL DEFAULT 5;");
            Console.WriteLine("[DB] Added MaxReadingPoints to licenses.");
        }
        catch { /* already exists */ }

        // Add IsActive to antennas
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `antennas` ADD COLUMN `IsActive` TINYINT(1) NOT NULL DEFAULT 1;");
            Console.WriteLine("[DB] Added IsActive to antennas.");
        }
        catch { /* already exists */ }

        // Rename Domain → CompanyName on licenses if old column exists
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE `licenses` CHANGE COLUMN `Domain` `CompanyName` VARCHAR(255) NULL;
            ");
            Console.WriteLine("[DB] Renamed licenses.Domain to CompanyName.");
        }
        catch { /* already renamed or never existed — safe to ignore */ }

        // Add CompanyName if it doesn't exist yet (fresh installs that skipped Domain)
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE `licenses` ADD COLUMN `CompanyName` VARCHAR(255) NULL;
            ");
            Console.WriteLine("[DB] Added CompanyName column to licenses.");
        }
        catch { /* already exists — safe to ignore */ }

        // Add IsActive column to users if it doesn't exist yet
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE `users` ADD COLUMN `IsActive` TINYINT(1) NOT NULL DEFAULT 1;
            ");
            Console.WriteLine("[DB] Added IsActive column to users.");
        }
        catch { /* column already exists — safe to ignore */ }

        // Add Language column to users if it doesn't exist yet
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `users` ADD COLUMN `Language` VARCHAR(5) NOT NULL DEFAULT 'en';");
            Console.WriteLine("[DB] Added Language column to users.");
        }
        catch { /* column already exists */ }

        // Fix Language default from 'sl' to 'en' for installs that had the old default
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `users` MODIFY COLUMN `Language` VARCHAR(5) NOT NULL DEFAULT 'en';");
            Console.WriteLine("[DB] Updated Language column default to 'en'.");
        }
        catch { /* safe to ignore */ }

        // Add LicenseId columns to tenant-scoped tables
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `users` ADD COLUMN `LicenseId` INT NULL;"); } catch { /* already exists */ }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `ASSET` ADD COLUMN `LicenseId` INT NULL;"); } catch { /* already exists */ }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `zones` ADD COLUMN `LicenseId` INT NULL;"); } catch { /* already exists */ }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `readers` ADD COLUMN `LicenseId` INT NULL;"); } catch { /* already exists */ }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `TAG` ADD COLUMN `LicenseId` INT NULL;"); } catch { /* already exists */ }
        // Add LicenseId to Roles (null = system role, shared; non-null = tenant custom role)
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Roles` ADD COLUMN `LicenseId` INT NULL;"); Console.WriteLine("[DB] Added LicenseId to Roles."); } catch { /* already exists */ }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE `licenses` ADD COLUMN `ActivatedAt` DATETIME NULL;"); Console.WriteLine("[DB] Added ActivatedAt to licenses."); } catch { /* already exists */ }
        Console.WriteLine("[DB] LicenseId columns ensured.");

        // ── Module tables ──────────────────────────────────────────────────────
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `modules` (
                `id`          INT AUTO_INCREMENT PRIMARY KEY,
                `Code`        VARCHAR(50)  NOT NULL,
                `Name`        VARCHAR(100) NOT NULL,
                `Description` VARCHAR(500) NULL,
                `Icon`        VARCHAR(100) NULL,
                `IsCore`      TINYINT(1)   NOT NULL DEFAULT 0,
                UNIQUE INDEX `uix_module_code` (`Code`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `license_modules` (
                `id`              INT AUTO_INCREMENT PRIMARY KEY,
                `LicenseId`       INT         NOT NULL,
                `ModuleCode`      VARCHAR(50)  NOT NULL,
                `EnabledAt`       DATETIME     NOT NULL,
                `EnabledByUserId` INT          NULL,
                UNIQUE INDEX `uix_lic_mod` (`LicenseId`, `ModuleCode`),
                CONSTRAINT `fk_licmod_license` FOREIGN KEY (`LicenseId`) REFERENCES `licenses`(`id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        Console.WriteLine("[DB] Module tables ensured.");

        // ── Laundry tables ─────────────────────────────────────────────────────
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `laundry_items` (
                `id`         INT AUTO_INCREMENT PRIMARY KEY,
                `LicenseId`  INT           NOT NULL,
                `OwnerId`    INT           NULL,
                `Name`       VARCHAR(200)  NOT NULL,
                `Category`   VARCHAR(50)   NULL,
                `TagId`      INT           NULL,
                `Status`     VARCHAR(50)   NOT NULL DEFAULT 'active',
                `Notes`      VARCHAR(1000) NULL,
                `CreatedAt`  DATETIME      NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `laundry_bins` (
                `id`              INT AUTO_INCREMENT PRIMARY KEY,
                `LicenseId`       INT          NOT NULL,
                `Label`           VARCHAR(100) NOT NULL,
                `Status`          VARCHAR(30)  NOT NULL DEFAULT 'open',
                `OpenedAt`        DATETIME     NOT NULL,
                `ClosedAt`        DATETIME     NULL,
                `OpenedByUserId`  INT          NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `laundry_bin_items` (
                `id`              INT AUTO_INCREMENT PRIMARY KEY,
                `BinId`           INT      NOT NULL,
                `ItemId`          INT      NOT NULL,
                `ScannedAt`       DATETIME NOT NULL,
                `ScannedByUserId` INT      NULL,
                CONSTRAINT `fk_lbi_bin`  FOREIGN KEY (`BinId`)  REFERENCES `laundry_bins`(`id`)  ON DELETE CASCADE,
                CONSTRAINT `fk_lbi_item` FOREIGN KEY (`ItemId`) REFERENCES `laundry_items`(`id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `laundry_item_events` (
                `id`         INT AUTO_INCREMENT PRIMARY KEY,
                `ItemId`     INT           NOT NULL,
                `WorkerId`   INT           NULL,
                `FromStatus` VARCHAR(50)   NULL,
                `ToStatus`   VARCHAR(50)   NOT NULL,
                `Notes`      VARCHAR(1000) NULL,
                `CreatedAt`  DATETIME      NOT NULL,
                CONSTRAINT `fk_lie_item` FOREIGN KEY (`ItemId`) REFERENCES `laundry_items`(`id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `laundry_sets` (
                `id`                INT AUTO_INCREMENT PRIMARY KEY,
                `LicenseId`         INT      NOT NULL,
                `OwnerId`           INT      NOT NULL,
                `AssembledByUserId` INT      NULL,
                `AssembledAt`       DATETIME NOT NULL,
                `PickedUpAt`        DATETIME NULL,
                `PickedUpByUserId`  INT      NULL,
                `Status`            VARCHAR(30) NOT NULL DEFAULT 'ready'
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `laundry_set_items` (
                `id`     INT AUTO_INCREMENT PRIMARY KEY,
                `SetId`  INT NOT NULL,
                `ItemId` INT NOT NULL,
                CONSTRAINT `fk_lsi_set`  FOREIGN KEY (`SetId`)  REFERENCES `laundry_sets`(`id`)  ON DELETE CASCADE,
                CONSTRAINT `fk_lsi_item` FOREIGN KEY (`ItemId`) REFERENCES `laundry_items`(`id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        Console.WriteLine("[DB] Laundry tables ensured.");

        // SuperAdmin users table
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS `superadmin_users` (
                `id`           INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                `Email`        VARCHAR(255) NOT NULL UNIQUE,
                `PasswordHash` TEXT NOT NULL,
                `CreatedAt`    DATETIME NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");
        Console.WriteLine("[DB] superadmin_users table ensured.");

        // Seed default SuperAdmin user if none exist
        if (!await db.SuperAdminUsers.AnyAsync())
        {
            db.SuperAdminUsers.Add(new SuperAdminUser
            {
                Email        = "jakob.sink24@gmail.com",
                PasswordHash = Signalko.Web.Services.PasswordHasher.Hash("kosrkasi"),
                CreatedAt    = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] SuperAdmin user jakob.sink24@gmail.com created.");
        }

        // Unique constraint on LicenseKey
        try
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE `licenses` ADD UNIQUE INDEX `uix_license_key` (`LicenseKey`);");
            Console.WriteLine("[DB] Added UNIQUE index on licenses.LicenseKey.");
        }
        catch { /* already exists */ }

        // Add foreign key constraints for LicenseId (safe — only if not already present)
        var fkTables = new[] { ("users", "fk_users_license"), ("ASSET", "fk_asset_license"),
                                ("zones", "fk_zones_license"), ("readers", "fk_readers_license"), ("TAG", "fk_tag_license") };
        foreach (var (tbl, fkName) in fkTables)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync($@"
                    ALTER TABLE `{tbl}`
                    ADD CONSTRAINT `{fkName}` FOREIGN KEY (`LicenseId`) REFERENCES `licenses`(`id`) ON DELETE SET NULL;
                ");
                Console.WriteLine($"[DB] Added FK {fkName}.");
            }
            catch { /* FK already exists or table not ready — safe to ignore */ }
        }

        // Seed roles
        if (!await db.Roles.AnyAsync())
        {
            db.Roles.AddRange(
                new UserRole { Name = "Admin" },
                new UserRole { Name = "User"  }
            );
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] Roles created.");
        }

        // Recovery: if no user has Admin role, assign Admin to the first registered user
        var adminRoleCheck = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
        if (adminRoleCheck != null)
        {
            var hasAdmin = await db.users.AnyAsync(u => u.RoleId == adminRoleCheck.id);
            if (!hasAdmin)
            {
                var firstUser = await db.users.OrderBy(u => u.id).FirstOrDefaultAsync();
                if (firstUser != null)
                {
                    firstUser.RoleId = adminRoleCheck.id;
                    await db.SaveChangesAsync();
                    Console.WriteLine($"[Seed] Recovery: no admin found — assigned Admin role to user '{firstUser.Email}' (id={firstUser.id}).");
                }
            }
        }

        // Seed permissions
        var allPerms = new (string Code, string Label, string Category)[]
        {
            ("assets.view",     "Ogled sredstev",           "Sredstva"),
            ("assets.edit",     "Urejanje sredstev",        "Sredstva"),
            ("loans.view",      "Ogled izposoj",            "Izposoja"),
            ("loans.create",    "Nova izposoja",            "Izposoja"),
            ("loans.return",    "Vrnitev sredstev",         "Izposoja"),
            ("users.view",      "Ogled uporabnikov",        "Uporabniki"),
            ("users.manage",    "Upravljanje uporabnikov",  "Uporabniki"),
            ("readers.view",    "Ogled čitalcev",           "Strojna oprema"),
            ("readers.manage",  "Upravljanje čitalcev",     "Strojna oprema"),
            ("zones.view",      "Ogled con",                "Strojna oprema"),
            ("zones.manage",    "Upravljanje con",          "Strojna oprema"),
            ("antennas.view",   "Ogled anten",              "Strojna oprema"),
            ("antennas.manage", "Upravljanje anten",        "Strojna oprema"),
            ("presence.view",   "Ogled prisotnosti",        "Prisotnost"),
            ("presence.manage", "Admin prisotnost",         "Prisotnost"),
            ("tags.view",       "Ogled RFID tagov",         "RFID"),
            ("roles.manage",    "Upravljanje pravic",       "Sistem"),
            ("page.assets",     "Stran: Sredstva",          "Strani (dostop)"),
            ("page.loans",      "Stran: Izposoja",          "Strani (dostop)"),
            ("page.tags",       "Stran: RFID Tagi",         "Strani (dostop)"),
            ("page.presence",   "Stran: Prisotnost",        "Strani (dostop)"),
            ("page.users",      "Stran: Uporabniki",        "Strani (dostop)"),
            ("page.readers",    "Stran: Čitalci",           "Strani (dostop)"),
            ("page.antennas",   "Stran: Antene",            "Strani (dostop)"),
            ("page.zones",      "Stran: Cone",              "Strani (dostop)"),
            ("page.roles",           "Stran: Vloge",                  "Strani (dostop)"),
            ("page.presenceadmin",  "Stran: Prisotnost — nadzor",    "Strani (dostop)"),
            ("license.view",        "Ogled licence",                 "Sistem"),
            ("license.manage",      "Upravljanje licence",           "Sistem"),
            ("page.license",        "Stran: Licenca",                "Strani (dostop)"),
        };
        foreach (var p in allPerms)
        {
            if (!await db.Permissions.AnyAsync(x => x.Code == p.Code))
                db.Permissions.Add(new Permission { Code = p.Code, Label = p.Label, Category = p.Category });
        }
        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Permissions seeded.");

        // Give Admin role all permissions
        var adminRoleP = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
        if (adminRoleP != null)
        {
            var existingPids = await db.RolePermissions
                .Where(rp => rp.RoleId == adminRoleP.id)
                .Select(rp => rp.PermissionId)
                .ToListAsync();
            var allPermEntities = await db.Permissions.ToListAsync();
            foreach (var perm in allPermEntities)
                if (!existingPids.Contains(perm.id))
                    db.RolePermissions.Add(new RolePermission { RoleId = adminRoleP.id, PermissionId = perm.id });
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] Admin permissions assigned.");
        }

        // Seed User role defaults ONLY if it currently has 0 permissions (fresh install).
        // If it already has any permissions, it was manually configured — don't touch it.
        var userRoleP = await db.Roles.FirstOrDefaultAsync(r => r.Name == "User");
        if (userRoleP != null)
        {
            var existingCount = await db.RolePermissions
                .CountAsync(rp => rp.RoleId == userRoleP.id);
            if (existingCount == 0)
            {
                var defaultCodes = new[] {
                    "assets.view", "loans.view", "loans.create", "loans.return", "tags.view",
                    "page.assets", "page.loans", "page.tags", "page.presence"
                };
                var defaultPerms = await db.Permissions
                    .Where(p => defaultCodes.Contains(p.Code))
                    .ToListAsync();
                foreach (var p in defaultPerms)
                    db.RolePermissions.Add(new RolePermission { RoleId = userRoleP.id, PermissionId = p.id });
                await db.SaveChangesAsync();
                Console.WriteLine($"[Seed] User role: seeded {defaultPerms.Count} default permissions (fresh install).");
            }
            else
            {
                Console.WriteLine($"[Seed] User role: has {existingCount} permissions, skipping seed.");
            }
        }

        // Seed license if none exists
        if (!await db.Licenses.AnyAsync())
        {
            db.Licenses.Add(new License
            {
                LicenseKey        = LicenseController.GenerateLicenseKey(),
                MaxUsers          = 10,
                MaxReadingPoints  = 5,
                CompanyName       = null,
                CreatedAt         = DateTime.UtcNow,
                UpdatedAt         = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] License created.");
        }

        // Seed modules
        var moduleSeeds = new (string Code, string Name, string Desc, string Icon, bool IsCore)[]
        {
            ("loans",    "Izposoja",   "Izposoja in vrnitev sredstev z RFID sledenjem",           "📦", true),
            ("presence", "Prisotnost", "Evidenca prisotnosti zaposlenih z RFID karticami",         "🕐", true),
            ("laundry",  "Pralnica",   "Sledenje perilom skozi pranje, šivanje in kompletiranje",  "👕", false),
        };
        foreach (var m in moduleSeeds)
        {
            if (!await db.Modules.AnyAsync(x => x.Code == m.Code))
                db.Modules.Add(new Module { Code = m.Code, Name = m.Name, Description = m.Desc, Icon = m.Icon, IsCore = m.IsCore });
        }
        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Modules seeded.");

        // Auto-enable core modules for every license that doesn't have them yet
        var allLicenseIds = await db.Licenses.Select(l => l.id).ToListAsync();
        var coreModuleCodes = await db.Modules.Where(m => m.IsCore).Select(m => m.Code).ToListAsync();
        foreach (var licId in allLicenseIds)
        {
            foreach (var code in coreModuleCodes)
            {
                if (!await db.LicenseModules.AnyAsync(lm => lm.LicenseId == licId && lm.ModuleCode == code))
                    db.LicenseModules.Add(new LicenseModule { LicenseId = licId, ModuleCode = code, EnabledAt = DateTime.UtcNow });
            }
        }
        await db.SaveChangesAsync();
        Console.WriteLine("[Seed] Core modules enabled for all licenses.");

        // Seed laundry permissions
        var laundryPerms = new (string Code, string Label, string Category)[]
        {
            ("laundry.view",    "Ogled pralnice",          "Pralnica"),
            ("laundry.deposit", "Sprejem perila",          "Pralnica"),
            ("laundry.process", "Obdelava perila",         "Pralnica"),
            ("laundry.manage",  "Upravljanje pralnice",    "Pralnica"),
            ("page.laundry",    "Stran: Pralnica",         "Strani (dostop)"),
        };
        foreach (var p in laundryPerms)
        {
            if (!await db.Permissions.AnyAsync(x => x.Code == p.Code))
                db.Permissions.Add(new Permission { Code = p.Code, Label = p.Label, Category = p.Category });
        }
        await db.SaveChangesAsync();

        // Migrate existing data to first license (assigns LicenseId to rows that don't have one yet)
        var firstLicenseId = await db.Licenses.AsNoTracking().OrderBy(l => l.id).Select(l => l.id).FirstOrDefaultAsync();
        if (firstLicenseId > 0)
        {
            await db.Database.ExecuteSqlRawAsync($"UPDATE `users`   SET `LicenseId` = {firstLicenseId} WHERE `LicenseId` IS NULL;");
            await db.Database.ExecuteSqlRawAsync($"UPDATE `ASSET`   SET `LicenseId` = {firstLicenseId} WHERE `LicenseId` IS NULL;");
            await db.Database.ExecuteSqlRawAsync($"UPDATE `zones`   SET `LicenseId` = {firstLicenseId} WHERE `LicenseId` IS NULL;");
            await db.Database.ExecuteSqlRawAsync($"UPDATE `readers` SET `LicenseId` = {firstLicenseId} WHERE `LicenseId` IS NULL;");
            await db.Database.ExecuteSqlRawAsync($"UPDATE `TAG`     SET `LicenseId` = {firstLicenseId} WHERE `LicenseId` IS NULL;");
            Console.WriteLine($"[DB] Existing data migrated to license {firstLicenseId}.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[DB] FAILED: {ex.GetType().Name}: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"[DB] Inner: {ex.InnerException.Message}");
        throw;
    }
}

// ── Seed test presence data (background, non-critical) ────────────────────────
static async Task SeedTestDataAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var now = DateTime.UtcNow;
        var schedule = new[]
        {
            (daysBack: 7, inH: 8,  inM: 00, outH: 17, outM: 00),
            (daysBack: 6, inH: 8,  inM: 45, outH: 16, outM: 30),
            (daysBack: 5, inH: 9,  inM: 10, outH: 17, outM: 55),
            (daysBack: 4, inH: 8,  inM: 20, outH: 18, outM: 05),
            (daysBack: 3, inH: 7,  inM: 50, outH: 17, outM: 40),
            (daysBack: 2, inH: 8,  inM: 35, outH: 16, outM: 50),
            (daysBack: 1, inH: 8,  inM: 15, outH: 17, outM: 20),
        };

        // Ensure every user has at least some presence entries
        var allUsers = await db.users.ToListAsync();
        int added = 0;
        foreach (var user in allUsers)
        {
            var hasAny = await db.UserPresences.AnyAsync(p => p.UserId == user.id);
            if (!hasAny)
            {
                foreach (var s in schedule)
                {
                    var day = now.Date.AddDays(-s.daysBack);
                    db.UserPresences.Add(new UserPresence { UserId = user.id, Type = "IN",  ScannedAt = day.AddHours(s.inH).AddMinutes(s.inM) });
                    db.UserPresences.Add(new UserPresence { UserId = user.id, Type = "OUT", ScannedAt = day.AddHours(s.outH).AddMinutes(s.outM) });
                }
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Presence test data seeded for {added} user(s).");
        }
        else
        {
            Console.WriteLine("[Seed] Presence data already exists for all users, skipping.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] Test data warning: {ex.Message}");
    }
}
