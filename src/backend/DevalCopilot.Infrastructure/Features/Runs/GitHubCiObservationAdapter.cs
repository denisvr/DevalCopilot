using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Observes the identity, the exact branch tip and GitHub's aggregate check state of that tip commit for one GitHub repository (ADR-0044)
/// with exactly one <c>gh api graphql --hostname github.com --method POST -f query=&lt;constant&gt; -f owner=&lt;owner&gt;
/// -f name=&lt;name&gt; -f ref=refs/heads/&lt;branch&gt;</c>, composed on the existing <see cref="IProcessExecutionAdapter"/> inside the host
/// scratch directory (never a repository, shell, jq or second process engine). The GraphQL document is a constant read-only query; the
/// stored names and the bounded owned branch travel only as separate raw-string variables (<c>-f</c>, never <c>-F</c>), so nothing is
/// interpolated, coerced or re-parsed. The HTTP POST only transports the query; no mutation, other endpoint, pagination, cache or retry
/// exists. The rollup selected is the commit's single <c>statusCheckRollup.state</c>: GitHub's aggregate of check runs and statuses, never
/// a list of checks, workflow runs or a required-check decision.
///
/// <para>
/// The child receives only a short allowlist of non-secret OS, profile and temp variables plus fixed controls that disable prompts,
/// update notifications, telemetry, color and spinners. No ambient token, host, config directory, debug, proxy, browser or pager override
/// is forwarded, so this is the stored default context only. This type never reads an auth file or credential; standard error is
/// discarded unread.
/// </para>
///
/// <para>
/// A singleton instance permits one in-flight observation: a concurrent request is <see langword="null"/> immediately, with no queue,
/// retry or extra process, and the slot is released after every outcome. Only the whole exact JSON
/// <c>{"data":{"repository":{"id","name","nameWithOwner","ref"}}}</c> is accepted, with <c>ref</c> either an explicit null or exactly
/// <c>{"name","prefix","target":{"__typename","oid","statusCheckRollup"}}</c> whose <c>statusCheckRollup</c> is an explicit null or exactly
/// <c>{"state"}</c> with one of the five literal GitHub states. GitHub's <c>qualifiedName</c> lookup can fall back to a short name, so the
/// request alone proves nothing: the prefix must be exactly <c>refs/heads/</c>, the name ordinally equal to the requested branch and the
/// target a Commit with a 40-character lowercase hexadecimal object ID. Errors, extensions, duplicate, missing or extra fields at any level,
/// escaped, malformed or oversized values, an unsupported state, a nonzero exit, a timeout, truncation, an inaccessible repository and a
/// response naming a different repository (compared ordinally, ignoring only ASCII case; never trimmed, decoded or adopted after a rename
/// or transfer) are <see langword="null"/>: neither nonexistence nor permission is inferred, a valid null ref means only that GitHub
/// returned no ref and a valid null rollup only that it returned no rollup. Only the caller's own cancellation and unexpected faults are
/// raised.
/// </para>
/// </summary>
public sealed class GitHubCiObservationAdapter : IGitHubCiObserver
{
    internal const string Query =
        "query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{state}}}}}}";

    private const string HeadsPrefix = "refs/heads/";

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
    private readonly Func<string> _scratchDirectory;
    private readonly Func<string, string?> _readEnvironmentVariable;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _probeSlot = new(1, 1);

    public GitHubCiObservationAdapter(IProcessExecutionAdapter processExecutionAdapter)
        : this(processExecutionAdapter, HostScratchDirectory.EnsureExists, System.Environment.GetEnvironmentVariable)
    {
    }

    internal GitHubCiObservationAdapter(
        IProcessExecutionAdapter processExecutionAdapter,
        Func<string> scratchDirectory,
        Func<string, string?> readEnvironmentVariable,
        TimeSpan? probeTimeout = null)
    {
        _processExecutionAdapter = processExecutionAdapter;
        _scratchDirectory = scratchDirectory;
        _readEnvironmentVariable = readEnvironmentVariable;
        _timeout = probeTimeout ?? DefaultTimeout;
    }

    public async Task<GitHubCiProbe?> ObserveAsync(
        string executablePath, string owner, string repository, string branchName, CancellationToken cancellationToken)
    {
        if (!GitHubRepositoryNamePolicy.IsOwner(owner) || !GitHubRepositoryNamePolicy.IsRepository(repository)
            || !GitHubOwnedBranchNamePolicy.IsOwnedBranch(branchName))
        {
            return null;
        }

        if (!_probeSlot.Wait(0, CancellationToken.None))
        {
            return null;
        }

        try
        {
            return await ProbeAsync(executablePath, owner, repository, branchName, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _probeSlot.Release();
        }
    }

    private async Task<GitHubCiProbe?> ProbeAsync(
        string executablePath, string owner, string repository, string branchName, CancellationToken cancellationToken)
    {
        var scratch = _scratchDirectory();
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = executablePath,
            Arguments =
            [
                "api", "graphql", "--hostname", "github.com", "--method", "POST",
                "-f", "query=" + Query, "-f", "owner=" + owner, "-f", "name=" + repository, "-f", "ref=refs/heads/" + branchName,
            ],
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
            // The OS could not start the vetted executable. Never the exception object or its message.
            return null;
        }
        catch (ArgumentException exception) when (
            exception.ParamName == nameof(ProcessExecutionRequest.ExecutablePath) && Path.IsPathFullyQualified(executablePath))
        {
            // The vetted absolute path no longer exists as a file, identified by parameter, not message.
            return null;
        }

        if (result.Outcome == ProcessExecutionOutcome.Cancelled)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (result.Outcome != ProcessExecutionOutcome.Exited || result.ExitCode != 0
            || result.StandardOutputTruncated || result.StandardErrorTruncated)
        {
            return null;
        }

        var probe = Interpret(result.StandardOutput, branchName);

        return probe is not null
            && string.Equals(probe.Owner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(probe.Repository, repository, StringComparison.OrdinalIgnoreCase)
                ? probe
                : null;
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

    /// <summary>Only the exact shape is accepted; null is unknown. Duplicate properties are rejected, not merged.</summary>
    private static GitHubCiProbe? Interpret(string standardOutput, string branchName)
    {
        // JsonProperty.Name decodes escaped keys, so a literal backslash anywhere (key or value) is refused before parsing.
        if (standardOutput.Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(standardOutput, StrictJson);
            var data = SingleObject(document.RootElement, "data");
            var repository = data is null ? null : SingleObject(data.Value, "repository");
            if (repository is null)
            {
                return null;
            }

            var properties = repository.Value.EnumerateObject().ToArray();
            if (properties.Length != 4)
            {
                return null;
            }

            var id = Text(properties, "id");
            var name = Text(properties, "name");
            var nameWithOwner = Text(properties, "nameWithOwner");
            if (id is null || name is null || nameWithOwner is null || !GitHubRepositoryNodeIdPolicy.IsNodeId(id)
                || !GitHubRepositoryNamePolicy.IsRepository(name))
            {
                return null;
            }

            var separator = nameWithOwner.IndexOf('/', StringComparison.Ordinal);
            if (separator < 0 || separator != nameWithOwner.LastIndexOf('/'))
            {
                return null;
            }

            var owner = nameWithOwner[..separator];
            if (!GitHubRepositoryNamePolicy.IsOwner(owner) || !string.Equals(nameWithOwner[(separator + 1)..], name, StringComparison.Ordinal))
            {
                return null;
            }

            var refs = properties.Where(property => property.Name == "ref").ToArray();
            if (refs.Length != 1)
            {
                return null;
            }

            if (refs[0].Value.ValueKind == JsonValueKind.Null)
            {
                return new GitHubCiProbe(id, owner, name, null, null);
            }

            var branch = InterpretRef(refs[0].Value, branchName);

            return branch is null ? null : new GitHubCiProbe(id, owner, name, branch.Value.Ref, branch.Value.RollupState);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The ref is the recorded branch head commit or nothing: GitHub's qualified-name lookup can fall back to other names, so
    /// the prefix, the name, the target and the rollup are each proved exactly.</summary>
    private static (GitHubCiRef Ref, string? RollupState)? InterpretRef(JsonElement reference, string branchName)
    {
        if (reference.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = reference.EnumerateObject().ToArray();
        if (properties.Length != 3)
        {
            return null;
        }

        var name = Text(properties, "name");
        var prefix = Text(properties, "prefix");
        var targets = properties.Where(property => property.Name == "target").ToArray();
        if (name is null || prefix is null || targets.Length != 1 || targets[0].Value.ValueKind != JsonValueKind.Object
            || !string.Equals(prefix, HeadsPrefix, StringComparison.Ordinal)
            || !string.Equals(name, branchName, StringComparison.Ordinal))
        {
            return null;
        }

        var target = targets[0].Value.EnumerateObject().ToArray();
        if (target.Length != 3)
        {
            return null;
        }

        var typeName = Text(target, "__typename");
        var objectId = Text(target, "oid");
        if (!string.Equals(typeName, "Commit", StringComparison.Ordinal) || !GitHubObservationTarget.IsCommitSha(objectId))
        {
            return null;
        }

        var rollups = target.Where(property => property.Name == "statusCheckRollup").ToArray();
        if (rollups.Length != 1)
        {
            return null;
        }

        if (rollups[0].Value.ValueKind == JsonValueKind.Null)
        {
            return (new GitHubCiRef(name, objectId!), null);
        }

        if (rollups[0].Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var rollup = rollups[0].Value.EnumerateObject().ToArray();
        var state = rollup.Length == 1 ? Text(rollup, "state") : null;

        return GitHubCiRollupStatePolicy.IsRollupState(state) ? (new GitHubCiRef(name, objectId!), state) : null;
    }

    private static JsonElement? SingleObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = element.EnumerateObject().ToArray();

        return properties.Length == 1 && properties[0].Name == propertyName && properties[0].Value.ValueKind == JsonValueKind.Object
            ? properties[0].Value
            : null;
    }

    /// <summary>A string property that occurs exactly once and carries no JSON escape (so nothing is decoded).</summary>
    private static string? Text(JsonProperty[] properties, string name)
    {
        var matches = properties.Where(property => property.Name == name).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = matches[0].Value.GetString();

        return value is not null && !matches[0].Value.GetRawText().Contains('\\', StringComparison.Ordinal) ? value : null;
    }
}
