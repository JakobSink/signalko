using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Signalko.Core;
using Signalko.Infrastructure;
using Signalko.Web.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var cs = builder.Configuration.GetConnectionString("Default")!;
builder.Services.AddDbContext<AppDbContext>(opt =>
{
    opt.UseMySql(cs, ServerVersion.AutoDetect(cs),
        my => my.MigrationsAssembly("Signalko.Infrastructure"));
});

builder.Services.AddScoped<JwtTokenService>();

var jwtKey = builder.Configuration["Jwt:Key"]!;
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
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var port = Environment.GetEnvironmentVariable("PORT") ?? "5072";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddSingleton<Signalko.Web.Services.IngestThrottle>();
builder.Services.AddSingleton<Signalko.Web.Services.IngestProfileState>();
builder.Services.AddScoped<Signalko.Web.Services.PresenceService>();

builder.Services.AddCors(o => o.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── Database seeding ──────────────────────────────────────────────────────────
await SeedAsync(app);

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.UseStaticFiles();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTime.UtcNow }));

app.MapFallback(async ctx =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

app.Run();

// ── Seed helper ───────────────────────────────────────────────────────────────
static async Task SeedAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    try
    {
        // Create exchange_requests table — column names match EF Core entity (snake_case via [Column] attributes)
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

        // Add missing columns if table existed without them (safe ALTER)
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE exchange_requests
                    MODIFY COLUMN Status      VARCHAR(20)  NOT NULL DEFAULT 'pending',
                    MODIFY COLUMN Message     VARCHAR(500) NULL,
                    MODIFY COLUMN created_at  DATETIME     NOT NULL,
                    MODIFY COLUMN responded_at DATETIME    NULL;
            ");
        }
        catch { /* already correct */ }

        // ── CardEpc on users ──────────────────────────────────────────────────
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `CardEpc` varchar(128) NULL;");
        }
        catch { /* already there */ }

        // ── user_presence table ───────────────────────────────────────────────
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

        // Seed user roles (Roles table)
        if (!await db.Roles.AnyAsync())
        {
            db.Roles.AddRange(
                new UserRole { Name = "Admin" },
                new UserRole { Name = "User"  }
            );
            await db.SaveChangesAsync();
        }

        // ── Seed test presence data (only if table is empty) ─────────────────
        var presenceCount = await db.UserPresences.CountAsync();
        if (presenceCount == 0)
        {
            var allUsers = await db.users.ToListAsync();
            var now = DateTime.UtcNow;
            // 5 workdays back: IN at ~8h, OUT at ~17h with small variations
            var schedule = new[]
            {
                (daysBack: 5, inH: 8,  inM: 00, outH: 17, outM: 00),
                (daysBack: 4, inH: 8,  inM: 30, outH: 16, outM: 45),
                (daysBack: 3, inH: 9,  inM: 05, outH: 17, outM: 30),
                (daysBack: 2, inH: 7,  inM: 50, outH: 18, outM: 10),
                (daysBack: 1, inH: 8,  inM: 15, outH: 17, outM: 20),
            };
            foreach (var user in allUsers)
            {
                foreach (var s in schedule)
                {
                    var day = now.Date.AddDays(-s.daysBack);
                    db.UserPresences.Add(new UserPresence
                    {
                        UserId    = user.id,
                        Type      = "IN",
                        ScannedAt = day.AddHours(s.inH).AddMinutes(s.inM),
                    });
                    db.UserPresences.Add(new UserPresence
                    {
                        UserId    = user.id,
                        Type      = "OUT",
                        ScannedAt = day.AddHours(s.outH).AddMinutes(s.outM),
                    });
                }
            }
            if (allUsers.Count > 0)
            {
                await db.SaveChangesAsync();
                Console.WriteLine($"[Seed] Presence test data created for {allUsers.Count} users.");
            }
        }

        // Seed default admin user if no admin exists
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
        if (adminRole != null && !await db.users.AnyAsync(u => u.RoleId == adminRole.id))
        {
            // Generate unique CardID for admin
            var rng    = new Random();
            var cardId = "000001";
            for (int i = 0; i < 30 && await db.users.AnyAsync(u => u.CardID == cardId); i++)
                cardId = rng.Next(0, 1_000_000).ToString("D6");

            db.users.Add(new User
            {
                Name     = "Admin",
                Surname  = "Signalko",
                Email    = "admin@signalko.si",
                Password = PasswordHasher.Hash("admin123"),
                CardID   = cardId,
                RoleId   = adminRole.id,
            });
            await db.SaveChangesAsync();
            Console.WriteLine($"[Seed] Admin user created — email: admin@signalko.si, password: admin123, card: {cardId}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] Warning: {ex.Message}");
    }
}
