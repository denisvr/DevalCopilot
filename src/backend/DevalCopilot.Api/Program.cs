using Devalente.Shared.AspNetCore.Security;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.RealTime;
using DevalCopilot.Api.Security;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.StartSimulatedRun;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Security.Ports;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Loopback only: this host is a privileged local process and is never internet-facing.
if (builder.Configuration["Urls"] is null && Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
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

// Narrowest possible allowlist: no origin is permitted unless explicitly configured, and
// today only the browser-hosted development/test composition configures one at all. The
// eventual Tauri-hosted production frontend is not a separate browser origin, so it does
// not need this policy.
const string BrowserHostedDevelopmentCorsPolicy = "BrowserHostedDevelopment";
var allowedBrowserOrigin = builder.Configuration["Cors:AllowedOrigin"];
if (!string.IsNullOrEmpty(allowedBrowserOrigin))
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

if (!string.IsNullOrEmpty(allowedBrowserOrigin))
{
    app.UseCors(BrowserHostedDevelopmentCorsPolicy);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RunNotificationHub>("/hubs/run", options => options.Transports = HttpTransportType.LongPolling);

await app.RunAsync();

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
