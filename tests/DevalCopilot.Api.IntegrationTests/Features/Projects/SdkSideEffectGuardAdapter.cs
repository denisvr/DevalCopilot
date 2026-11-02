using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0074 (WEP-F6) pre-launch control for tests that run the machine's real .NET SDK. The SDK's first-run experience appends the owned
/// tools directory to the persistent user PATH and generates an ASP.NET development certificate in the user's certificate store, and both
/// outlive the owned scratch; a persistent write cannot be undone by test cleanup. Immediately before the real adapter would start a
/// <c>dotnet.exe</c> child, this guard inspects the exact environment that child would receive and refuses, without starting anything, when
/// either flag is not the literal <c>false</c>. A refused request throws the way an unavailable invocation does, so the supervisor records a
/// failed execution with no process, and <see cref="Refusals"/> lets the calling test fail loudly instead of passing on that failure.
/// It reads only the request; it never reads, writes or restores the user PATH or any certificate store.
/// </summary>
internal sealed class SdkSideEffectGuardAdapter(IProcessExecutionAdapter inner) : IProcessExecutionAdapter
{
    internal static readonly string[] RequiredFalseFlags = ["DOTNET_ADD_GLOBAL_TOOLS_TO_PATH", "DOTNET_GENERATE_ASPNET_CERTIFICATE"];

    private int _launches;
    private int _refusals;
    private string? _lastRefusal;

    /// <summary>Requests that were handed to the real adapter.</summary>
    public int Launches => Volatile.Read(ref _launches);

    /// <summary>Requests refused before any child was started.</summary>
    public int Refusals => Volatile.Read(ref _refusals);

    /// <summary>The names (never values) of the flags that were missing or not <c>false</c> in the latest refusal.</summary>
    public string? LastRefusal => _lastRefusal;

    /// <summary>The flag names a <c>dotnet.exe</c> request violates; empty for a safe request and for any other executable.</summary>
    internal static IReadOnlyList<string> Violations(ProcessExecutionRequest request)
    {
        if (!string.Equals(Path.GetFileName(request.ExecutablePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var environment = new Dictionary<string, string>(request.EnvironmentVariables, StringComparer.OrdinalIgnoreCase);
        return RequiredFalseFlags
            .Where(flag => !environment.TryGetValue(flag, out var value) || !string.Equals(value, "false", StringComparison.Ordinal))
            .ToList();
    }

    public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
    {
        var violations = Violations(request);
        if (violations.Count > 0)
        {
            _lastRefusal = string.Join(",", violations);
            Interlocked.Increment(ref _refusals);
            throw new InvalidOperationException("The SDK side-effect flags are not both false; no child was started.");
        }

        Interlocked.Increment(ref _launches);
        return inner.ExecuteAsync(request, cancellationToken);
    }
}
