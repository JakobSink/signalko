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
                `Domain`      VARCHAR(255) NULL,
                `CreatedAt`   DATETIME     NOT NULL,
                `UpdatedAt`   DATETIME     NOT NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        ");

        // Add IsActive column to users if it doesn't exist yet
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE `users` ADD COLUMN `IsActive` TINYINT(1) NOT NULL DEFAULT 1;
            ");
            Console.WriteLine("[DB] Added IsActive column to users.");
        }
        catch { /* column already exists — safe to ignore */ }

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
                LicenseKey = LicenseController.GenerateLicenseKey(),
                MaxUsers   = 10,
                Domain     = null,
                CreatedAt  = DateTime.UtcNow,
                UpdatedAt  = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            Console.WriteLine("[Seed] License created.");
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
