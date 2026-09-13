using Devalente.Shared.AspNetCore.Security;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.RealTime;
using DevalCopilot.Api.Security;
using DevalCopilot.Api.Security.Bootstrap;
using DevalCopilot.Api.Security.Cors;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Security.Ports;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

// Explicit opt-in only: the packaged Tauri shell always passes this flag when it starts
// the sidecar. Nothing else (an ambient environment variable, a default) can enable the
// stdin bootstrap path, so `dotnet run`, Playwright, and the xUnit test host are
// unaffected and keep using the existing IConfiguration-based launch secret.
var bootstrapStdin = args.Contains("--bootstrap-stdin");

BootstrapReadResult? bootstrapReadResult = null;
var stdin = bootstrapStdin ? Console.OpenStandardInput() : null;
if (bootstrapStdin)
{
    var frameResult = await BootstrapFrameReader.ReadAsync(stdin!, CancellationToken.None);
    if (frameResult.IsFailure)
    {
        // Fail closed: never bind or serve. Only the generic error code reaches stderr —
        // never the raw bootstrap line, which could contain the secret.
        await Console.Error.WriteLineAsync($"Bootstrap failed: {frameResult.Errors[0].Code}");
        Environment.Exit(1);
        return;
    }

    bootstrapReadResult = frameResult.Value;
}

var builder = WebApplication.CreateBuilder(args);

if (bootstrapStdin)
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Configuration.AddInMemoryCollection(
        [new KeyValuePair<string, string?>("LaunchSession:Secret", bootstrapReadResult!.Frame.Secret)]);

    // stdout is reserved exclusively for the one BootstrapReadyMarker line below: nothing
    // here can corrupt or be confused with that protocol. Ordinary logs still reach
    // stderr, which the shell does not read as protocol input.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
}
// Loopback only: this host is a privileged local process and is never internet-facing.
else if (builder.Configuration["Urls"] is null && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
{
    builder.WebHost.UseUrls("http://127.0.0.1:5080");
}

// Resolved from IConfiguration at DbContext-construction time (after Build()), not read
// into a local variable early: a test host's ConfigureAppConfiguration override must be
// visible here, and it is only guaranteed to be merged by the time services are resolved.
builder.Services.AddDbContext<DevalCopilotDbContext>((provider, options) =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DevalCopilot") ?? DefaultConnectionString();
    options.UseSqlite(connectionString);
});
builder.Services.AddScoped<IDevalCopilotDbContext>(provider => provider.GetRequiredService<DevalCopilotDbContext>());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILaunchSessionAccessor, LaunchSessionAccessor>();
builder.Services.AddSingleton<ISimulatedAgentAdapter, DeterministicSimulatedAgentAdapter>();
builder.Services.AddScoped<IRunEventNotifier, SignalRRunEventNotifier>();
builder.Services.AddHostedService<SimulatedRunSupervisor>();

builder.Services.AddDevalenteMediator(typeof(StartSimulatedRunCommand).Assembly);
builder.Services.AddDevalenteRequestValidation(typeof(StartSimulatedRunCommand).Assembly);
builder.Services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();

builder.Services.AddControllers();
builder.Services.AddDevalenteMvcProblemDetails();
builder.Services.AddDevalenteOpenApi("DevalCopilot API");
builder.Services.AddSignalR();

builder.Services
    .AddAuthentication(LaunchSessionAuthenticationDefaults.AuthenticationScheme)
    .AddScheme<LaunchSessionAuthenticationOptions, LaunchSessionAuthenticationHandler>(
        LaunchSessionAuthenticationDefaults.AuthenticationScheme, _ => { });
builder.Services.AddDevalenteAuthorizationDefaults();

// Narrowest possible allowlist: no origin is permitted unless it is a specific, known
// trusted frontend. The Tauri shell's bundled WebView IS a separate browser origin from
// this loopback API (http://tauri.localhost when packaged, the Vite dev origin under
// `tauri dev`) — never AllowAnyOrigin(), and CORS is never treated as authorization: the
// per-launch Bearer secret is still required on every protected request regardless of
// which of these origins sent it.
const string TauriShellCorsPolicy = "TauriShell";
const string BrowserHostedDevelopmentCorsPolicy = "BrowserHostedDevelopment";
var allowedBrowserOrigin = builder.Configuration["Cors:AllowedOrigin"];

if (bootstrapStdin)
{
    // Both `tauri dev` and a packaged release binary launch the sidecar with the same
    // --bootstrap-stdin flag, and the host has no reliable way to tell which one started
    // it — so both of the shell's own possible origins are trusted together here, never a
    // third-party page.
    builder.Services.AddCors(options => options.AddPolicy(
        TauriShellCorsPolicy,
        policy => policy
            .WithOrigins(TrustedFrontendOrigins.PackagedWindowsWebView, TrustedFrontendOrigins.TauriDevelopmentWebView)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()));
}
else if (!string.IsNullOrEmpty(allowedBrowserOrigin))
{
    // SignalR's negotiate/LongPolling requests are sent with credentials included so the
    // launch secret can travel as an Authorization header rather than an access_token query
    // string. That requires AllowCredentials(), which is only safe alongside a fixed-origin
    // WithOrigins() allowlist (never AllowAnyOrigin()).
    builder.Services.AddCors(options => options.AddPolicy(
        BrowserHostedDevelopmentCorsPolicy,
        policy => policy.WithOrigins(allowedBrowserOrigin).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));
}

var app = builder.Build();

using (var startupScope = app.Services.CreateScope())
{
    var dbContext = startupScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
    await dbContext.Database.MigrateAsync();
    await ProjectFixture.EnsureSeededAsync(dbContext);
}

if (bootstrapStdin)
{
    app.UseCors(TauriShellCorsPolicy);
}
else if (!string.IsNullOrEmpty(allowedBrowserOrigin))
{
    app.UseCors(BrowserHostedDevelopmentCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RunNotificationHub>("/hubs/run", options => options.Transports = HttpTransportType.LongPolling);

if (bootstrapStdin)
{
    await app.StartAsync();

    var boundAddress = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
    var port = new Uri(boundAddress).Port;

    // The one and only stdout line this process ever writes in bootstrap mode: never the
    // secret, only bounded readiness information and the selected port.
    Console.WriteLine(BootstrapReadyMarker.Format(port));

    var stdinMonitorLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BootstrapStdinMonitor");
    _ = BootstrapStdinMonitor.RunAsync(
        stdin!,
        bootstrapReadResult!.Trailing,
        app.Lifetime,
        stdinMonitorLogger,
        app.Lifetime.ApplicationStopping);

    await app.WaitForShutdownAsync();
}
else
{
    await app.RunAsync();
}

static string DefaultConnectionString()
{
    var applicationDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevalCopilot");
    Directory.CreateDirectory(applicationDataDirectory);

    return $"Data Source={Path.Combine(applicationDataDirectory, "devalcopilot.db")}";
}

/// <summary>
/// Entry point marker so <c>WebApplicationFactory&lt;Program&gt;</c> can locate this
/// assembly's composition root from test projects.
/// </summary>
public partial class Program;
