using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Mangrove.Server;
using Mangrove.Server.Auth;
using Mangrove.Server.Data;
using Mangrove.Server.Hosting;
using Mangrove.Server.Readers;
using Mangrove.Server.Scanning;
using Mangrove.Server.Security;
using Mangrove.Server.Storage;
using Mangrove.Server.Storage.Smb;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

JwtSecurityTokenHandler.DefaultMapInboundClaims = false; // keep "sub"/"role" claim types intact

// Service-control CLI: `Mangrove.exe install|uninstall|start|stop|restart|status`.
if (ServiceInstaller.TryHandle(args, out var cliExit))
    return cliExit;

// When started by the Windows Service Control Manager the working directory is C:\Windows\System32.
// Anchor it to the executable folder so the DB/cache land next to the app, not in System32.
if (WindowsServiceHelpers.IsWindowsService())
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

// No-op unless launched as a Windows service; enables SCM integration + Event Log logging.
builder.Host.UseWindowsService(options => options.ServiceName = ServiceInstaller.ServiceName);

var startupLogger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Startup");

// Default port is 5173 (5000 is commonly taken by other apps). Override with MANGROVE_PORT in
// appsettings.json or the environment.
var port = builder.Configuration["MANGROVE_PORT"]
           ?? Environment.GetEnvironmentVariable("MANGROVE_PORT") ?? "5173";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ---- Runtime config (spec §12) ----
var paths = new ServerPaths(builder.Configuration);
var protector = CredentialProtector.FromEnvironment(builder.Configuration, startupLogger);
var jwtOptions = JwtOptions.FromConfiguration(builder.Configuration, startupLogger);

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(protector);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<SmbConnectionPool>();
builder.Services.AddSingleton<StorageProviderFactory>();
builder.Services.AddSingleton<ArchiveReader>();
builder.Services.AddSingleton<ImageFolderReader>();
builder.Services.AddSingleton<PdfPageReader>();
builder.Services.AddSingleton<EpubService>();
builder.Services.AddSingleton<ComicInfoReader>();
builder.Services.AddSingleton<ReaderService>();
builder.Services.AddSingleton<FilenameParser>();

// Quiet EF's per-statement SQL logging — at Info it floods the console and badly slows large scans.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

builder.Services.AddDbContext<MangroveDbContext>(options =>
    options.UseSqlite($"Data Source={paths.DbPath};Default Timeout=30"));

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<LibraryScanner>();

// Background scanning: scans run on a worker so HTTP requests return immediately.
builder.Services.AddSingleton<ScanJobQueue>();
builder.Services.AddHostedService<ScanBackgroundService>();

// ---- Auth ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = "unique_name",
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string WebCors = "web";
builder.Services.AddCors(o => o.AddPolicy(WebCors, p => p
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// ---- Migrate DB + seed roles on startup ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MangroveDbContext>();
    db.Database.Migrate();

    // WAL lets readers (auth/browse) proceed while a long scan holds the writer,
    // instead of failing with "database is locked". journal_mode persists in the DB file.
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    await using (var pragma = conn.CreateCommand())
    {
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=30000;";
        await pragma.ExecuteNonQueryAsync();
    }

    await SeedRolesAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI();

// Serve the built web UI (apps/web -> wwwroot) so a single deployment is browsable at the root.
// In dev the SPA runs under Vite (port 5173); in a published build wwwroot is populated by CI.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors(WebCors);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Client-side routes (e.g. /library/3, /reader/...) fall back to the SPA shell. API/Swagger
// routes are matched first, so this only catches browser navigations. No-op if wwwroot is empty.
app.MapFallbackToFile("index.html");

app.Logger.LogInformation("{AppName} v{Version} listening on http://0.0.0.0:{Port}. DB: {Db}, Cache: {Cache}",
    AppConstants.AppName, AppConstants.Version, port, paths.DbPath, paths.CacheDir);

try
{
    app.Run();
}
catch (IOException ex) when (IsAddressInUse(ex))
{
    // Most common deployment failure: another program already owns the port. When running as a
    // Windows service this otherwise surfaces only as an opaque APPCRASH in the Event Log, so log a
    // clear, actionable message (visible in Event Viewer) and exit cleanly.
    app.Logger.LogCritical(
        "Mangrove could not start because port {Port} is already in use by another program. " +
        "Set \"MANGROVE_PORT\" to a free port in appsettings.json next to Mangrove.exe, then restart " +
        "the service. Browse to http://localhost:<port> afterwards.",
        port);
    return 2;
}
return 0;

static bool IsAddressInUse(IOException ex) =>
    ex is Microsoft.AspNetCore.Connections.AddressInUseException ||
    ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException ||
    ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);

static async Task SeedRolesAsync(MangroveDbContext db)
{
    foreach (var type in Enum.GetValues<RoleType>())
    {
        if (!await db.Roles.AnyAsync(r => r.Type == type))
        {
            db.Roles.Add(new Role
            {
                Type = type,
                Name = type.ToString(),
                CanDownload = type != RoleType.ReadOnly,
                CanManageLibraries = type == RoleType.Admin,
            });
        }
    }
    await db.SaveChangesAsync();
}

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program { }
