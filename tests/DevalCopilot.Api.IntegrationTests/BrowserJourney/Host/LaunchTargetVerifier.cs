using System.Text.Json;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;

/// <summary>
/// Read-only proof, taken from the host's own readiness evidence, that the provider launch targets the workflow will start are
/// the owned doubles and nothing installed on the machine. It waits for the normal readiness supervisor to record both
/// capabilities, then writes a bounded verdict file the journey reads before it requests any agent stage. It never changes the
/// readiness evidence and never falls back to an installed provider.
/// </summary>
public static class LaunchTargetVerifier
{
    public const string VerdictFile = "launch-targets.json";

    public static async Task WriteVerdictAsync(IServiceProvider services, OwnedRootGuard root, CancellationToken cancellationToken)
    {
        var expected = new Dictionary<Capability, string>
        {
            [Capability.CodexCli] = Path.Combine(root.Bin, "codex.exe"),
            [Capability.ClaudeCli] = Path.Combine(root.Bin, "claude.exe"),
        };

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Dictionary<Capability, string?> resolved = [];
        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var snapshots = await db.HostCapabilitySnapshots.AsNoTracking()
                .Where(snapshot => snapshot.Capability == Capability.CodexCli || snapshot.Capability == Capability.ClaudeCli)
                .ToListAsync(cancellationToken);
            resolved = snapshots.ToDictionary(snapshot => snapshot.Capability, snapshot => snapshot.ResolvedExecutablePath);
            if (expected.Keys.All(capability => resolved.TryGetValue(capability, out var path) && !string.IsNullOrEmpty(path)))
            {
                break;
            }

            await Task.Delay(200, cancellationToken);
        }

        var verified = expected.All(pair =>
            resolved.TryGetValue(pair.Key, out var path)
            && string.Equals(path, pair.Value, StringComparison.OrdinalIgnoreCase));
        var verdict = new
        {
            verified,
            codexOwned = resolved.TryGetValue(Capability.CodexCli, out var codex)
                && string.Equals(codex, expected[Capability.CodexCli], StringComparison.OrdinalIgnoreCase),
            claudeOwned = resolved.TryGetValue(Capability.ClaudeCli, out var claude)
                && string.Equals(claude, expected[Capability.ClaudeCli], StringComparison.OrdinalIgnoreCase),
        };
        await root.WriteFixtureStateFileAsync(VerdictFile, JsonSerializer.Serialize(verdict), cancellationToken);
    }
}
