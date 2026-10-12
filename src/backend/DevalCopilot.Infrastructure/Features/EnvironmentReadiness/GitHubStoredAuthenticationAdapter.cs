using System.Text.Json;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;
using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Observes the DEFAULT STORED GitHub CLI authentication of github.com with exactly one
/// <c>gh auth status --hostname github.com --active --json hosts --jq &lt;selector&gt;</c>, composed on the existing
/// <see cref="IProcessExecutionAdapter"/> inside the host scratch directory (never a repository, shell or second process
/// engine). The constant selector reduces the CLI's answer to an <c>entries</c> array of <c>{active, state}</c> pairs (or
/// an <c>invalid</c> marker for an unexpected original shape, never an empty success), so no login, token, token source,
/// scope, error text or other account field ever reaches this process; stderr is discarded unread.
///
/// <para>
/// The child receives only a short allowlist of non-secret OS, profile and temp variables plus fixed controls that disable
/// prompts, update notifications, telemetry, color and spinners. No ambient token, host, config directory, debug, proxy,
/// browser or pager override is forwarded, so environment-token and custom-config contexts are deliberately NOT observed:
/// this is the stored default context, not the owner's complete ambient CLI session. This type never reads an auth file or
/// credential, and its result is never repository permission or publication authority.
/// </para>
///
/// <para>
/// A singleton instance permits one in-flight probe: a concurrent request is <see cref="GitHubStoredAuthenticationObservation.Unknown"/>
/// immediately, with no queue, retry or extra process. Exit zero alone is never authentication; only a coherent empty array,
/// or exactly one active entry in the closed <c>success</c>/<c>error</c> states, is an observation; a legitimately empty
/// <c>hosts</c> map (the CLI's answer when no host is configured) is the empty array. Everything else (timeout, failure,
/// nonzero exit, truncation, malformed, duplicate or extra fields, an OS start failure) is Unknown. Only the caller's own
/// cancellation is raised, and any other fault propagates: it is never converted into a status.
/// </para>
/// </summary>
public sealed class GitHubStoredAuthenticationAdapter : IGitHubStoredAuthenticationObserver
{
    internal const string PrivacySelector =
        "if (.hosts | type) == \"object\" and (.hosts | length) == 0 then {entries: []} "
        + "elif (.hosts | type) == \"object\" and (.hosts[\"github.com\"] | type) == \"array\" then "
        + "{entries: [.hosts[\"github.com\"][] | {active: (.active == true), "
        + "state: (if .state == \"success\" or .state == \"error\" then .state else \"other\" end)}]} "
        + "else {invalid: true} end";

    private static readonly string[] Arguments =
        ["auth", "status", "--hostname", "github.com", "--active", "--json", "hosts", "--jq", PrivacySelector];

    /// <summary>The only ambient values forwarded, each only when present and non-empty.</summary>
    private static readonly string[] ForwardedVariables =
        ["SystemRoot", "SystemDrive", "windir", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "HOMEDRIVE", "HOMEPATH", "USERNAME", "TEMP", "TMP"];

    private static readonly KeyValuePair<string, string>[] FixedControls =
    [
        new("GH_PROMPT_DISABLED", "1"),
        new("GH_NO_UPDATE_NOTIFIER", "1"),
        new("GH_NO_EXTENSION_UPDATE_NOTIFIER", "1"),
        new("GH_TELEMETRY", "false"),
        new("DO_NOT_TRACK", "1"),
        new("NO_COLOR", "1"),
        new("GH_SPINNER_DISABLED", "1"),
    ];

    private const int MaxBytesPerStream = 8 * 1024;
    private const int MaxTotalCapturedBytes = 16 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonDocumentOptions StrictJson = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8,
    };

    private readonly IProcessExecutionAdapter _processExecutionAdapter;
    private readonly TimeProvider _timeProvider;
    private readonly Func<string> _scratchDirectory;
    private readonly Func<string, string?> _readEnvironmentVariable;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _probeSlot = new(1, 1);

    public GitHubStoredAuthenticationAdapter(IProcessExecutionAdapter processExecutionAdapter, TimeProvider timeProvider)
        : this(processExecutionAdapter, timeProvider, HostScratchDirectory.EnsureExists, System.Environment.GetEnvironmentVariable)
    {
    }

    internal GitHubStoredAuthenticationAdapter(
        IProcessExecutionAdapter processExecutionAdapter,
        TimeProvider timeProvider,
        Func<string> scratchDirectory,
        Func<string, string?> readEnvironmentVariable,
        TimeSpan? probeTimeout = null)
    {
        _processExecutionAdapter = processExecutionAdapter;
        _timeProvider = timeProvider;
        _scratchDirectory = scratchDirectory;
        _readEnvironmentVariable = readEnvironmentVariable;
        _timeout = probeTimeout ?? DefaultTimeout;
    }

    public async Task<GitHubStoredAuthenticationObservation> ObserveAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!_probeSlot.Wait(0, CancellationToken.None))
        {
            return GitHubStoredAuthenticationObservation.Unknown;
        }

        try
        {
            return await ProbeAsync(executablePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _probeSlot.Release();
        }
    }

    private async Task<GitHubStoredAuthenticationObservation> ProbeAsync(string executablePath, CancellationToken cancellationToken)
    {
        var scratch = _scratchDirectory();
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = executablePath,
            Arguments = Arguments,
            WorkingDirectory = scratch,
            ApprovedRoot = scratch,
            EnvironmentVariables = BuildEnvironment(),
            Timeout = _timeout,
            MaxBytesPerStream = MaxBytesPerStream,
            MaxTotalCapturedBytes = MaxTotalCapturedBytes,
        };

        ProcessExecutionResult result;
        try
        {
            result = await _processExecutionAdapter.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The OS could not start the vetted executable (missing, inaccessible or not runnable). Never the exception
            // object or its message: it can carry paths or provider text.
            return GitHubStoredAuthenticationObservation.Unknown;
        }
        catch (ArgumentException exception) when (
            exception.ParamName == nameof(ProcessExecutionRequest.ExecutablePath) && Path.IsPathFullyQualified(executablePath))
        {
            // The vetted absolute path no longer exists as a file (the executor's own check), identified by parameter, not
            // message. Every other fault is a programming or environment fault and propagates.
            return GitHubStoredAuthenticationObservation.Unknown;
        }

        if (result.Outcome == ProcessExecutionOutcome.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.Outcome != ProcessExecutionOutcome.Exited || result.ExitCode != 0
            || result.StandardOutputTruncated || result.StandardErrorTruncated)
        {
            return GitHubStoredAuthenticationObservation.Unknown;
        }

        var status = Interpret(result.StandardOutput);

        return status is null
            ? GitHubStoredAuthenticationObservation.Unknown
            : new GitHubStoredAuthenticationObservation(status.Value, _timeProvider.GetUtcNow());
    }

    private Dictionary<string, string> BuildEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in ForwardedVariables)
        {
            var value = _readEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
            {
                environment[name] = value;
            }
        }

        foreach (var (name, value) in FixedControls)
        {
            environment[name] = value;
        }

        return environment;
    }

    /// <summary>Only the exact filtered shape is accepted; null is Unknown. Duplicate properties are rejected, not merged.</summary>
    private static GitHubStoredAuthenticationStatus? Interpret(string standardOutput)
    {
        try
        {
            using var document = JsonDocument.Parse(standardOutput, StrictJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 1 || properties[0].Name != "entries" || properties[0].Value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var entries = properties[0].Value.EnumerateArray().ToArray();
            if (entries.Length == 0)
            {
                return GitHubStoredAuthenticationStatus.NoActiveAccount;
            }

            return entries.Length == 1 ? InterpretEntry(entries[0]) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static GitHubStoredAuthenticationStatus? InterpretEntry(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = entry.EnumerateObject().ToArray();
        if (properties.Length != 2)
        {
            return null;
        }

        if (properties.Count(property => property.Name == "active") != 1 || properties.Count(property => property.Name == "state") != 1)
        {
            return null;
        }

        var active = properties.Single(property => property.Name == "active");
        var state = properties.Single(property => property.Name == "state");
        if (active.Value.ValueKind != JsonValueKind.True || state.Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return state.Value.GetString() switch
        {
            "success" => GitHubStoredAuthenticationStatus.Authenticated,
            "error" => GitHubStoredAuthenticationStatus.IssueReported,
            _ => null,
        };
    }
}
