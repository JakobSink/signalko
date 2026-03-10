using Microsoft.EntityFrameworkCore;
using Signalko.Infrastructure;
using Signalko.Infrastructure.Services;
using Signalko.Web.Services;

var builder = Host.CreateApplicationBuilder(args);

// 🔄 preberemo iz appsettings (ključ "DefaultConnection")
var cs = builder.Configuration.GetConnectionString("DefaultConnection")!;

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseMySql(cs, ServerVersion.AutoDetect(cs))
);

builder.Services.AddScoped<TagService>();
builder.Services.AddHostedService<ReaderSupervisor>();
builder.Services.AddHttpClient();

var host = builder.Build();
host.Run();
