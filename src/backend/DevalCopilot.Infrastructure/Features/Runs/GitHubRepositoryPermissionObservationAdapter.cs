using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubRepositoryPermissionObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Observes the identity (opaque ID and names) and the viewer permission GitHub reports for one GitHub repository (ADR-0049) with exactly one
/// <c>gh api graphql --hostname github.com --method POST -f query=&lt;constant&gt; -f owner=&lt;owner&gt; -f name=&lt;name&gt;</c>,
/// composed on the existing <see cref="IProcessExecutionAdapter"/> inside the host scratch directory (never a repository, shell, jq or
/// second process engine). The GraphQL document is a constant read-only query; the stored names travel only as separate raw-string
/// variables (<c>-f</c>, never <c>-F</c>), so nothing is interpolated, coerced or re-parsed. The HTTP POST only transports the query; no
/// mutation, other endpoint, pagination, cache or retry exists.
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
/// retry or extra process, and the slot is released after every outcome. Only the exact JSON
/// <c>{"data":{"repository":{"id","name","nameWithOwner","viewerPermission"}}}</c> is accepted, with <c>viewerPermission</c> an explicit
/// null or exactly one of the six case-sensitive <c>RepositoryPermission</c> literals; errors, extensions, duplicate, missing or extra
/// fields, partial data, null, malformed, escaped or oversized values, an unknown permission literal, a nonzero exit, a timeout,
/// truncation, a missing or inaccessible repository and a response naming a different repository (compared ordinally, ignoring only ASCII
/// case; never trimmed, decoded or adopted after a rename or transfer) are <see langword="null"/>. The permission is only reported, never
/// ranked or turned into an allowance; a null permission is not a denial. Only the caller's own cancellation and unexpected faults are
/// raised.
/// </para>
/// </summary>
public sealed class GitHubRepositoryPermissionObservationAdapter : IGitHubRepositoryPermissionObserver
{
    internal const string Query =
        "query($owner:String!,$name:String!){repository(owner:$owner,name:$name){id name nameWithOwner viewerPermission}}";

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

    public GitHubRepositoryPermissionObservationAdapter(IProcessExecutionAdapter processExecutionAdapter)
        : this(processExecutionAdapter, HostScratchDirectory.EnsureExists, System.Environment.GetEnvironmentVariable)
    {
    }

    internal GitHubRepositoryPermissionObservationAdapter(
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

    public async Task<GitHubRepositoryPermissionProbe?> ObserveAsync(
        string executablePath, string owner, string repository, CancellationToken cancellationToken)
    {
        if (!GitHubRepositoryNamePolicy.IsOwner(owner) || !GitHubRepositoryNamePolicy.IsRepository(repository))
        {
            return null;
        }

        if (!_probeSlot.Wait(0, CancellationToken.None))
        {
            return null;
        }

        try
        {
            return await ProbeAsync(executablePath, owner, repository, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _probeSlot.Release();
        }
    }

    private async Task<GitHubRepositoryPermissionProbe?> ProbeAsync(
        string executablePath, string owner, string repository, CancellationToken cancellationToken)
    {
        var scratch = _scratchDirectory();
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = executablePath,
            Arguments =
            [
                "api", "graphql", "--hostname", "github.com", "--method", "POST",
                "-f", "query=" + Query, "-f", "owner=" + owner, "-f", "name=" + repository,
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

        var identity = Interpret(result.StandardOutput);

        return identity is not null
            && string.Equals(identity.Owner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.Repository, repository, StringComparison.OrdinalIgnoreCase)
                ? identity
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
    private static GitHubRepositoryPermissionProbe? Interpret(string standardOutput)
    {
        // JsonProperty.Name decodes escaped keys and values, so a literal backslash anywhere is refused before parsing.
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

            var permissions = properties.Where(property => property.Name == "viewerPermission").ToArray();
            if (permissions.Length != 1)
            {
                return null;
            }

            // An explicit null is a reported fact; a missing member, any other type and any literal outside the six are refused whole.
            string? permission = null;
            if (permissions[0].Value.ValueKind == JsonValueKind.String)
            {
                permission = permissions[0].Value.GetString();
                if (!GitHubRepositoryPermissionPolicy.IsViewerPermission(permission))
                {
                    return null;
                }
            }
            else if (permissions[0].Value.ValueKind != JsonValueKind.Null)
            {
                return null;
            }

            var owner = nameWithOwner[..separator];

            return GitHubRepositoryNamePolicy.IsOwner(owner) && string.Equals(nameWithOwner[(separator + 1)..], name, StringComparison.Ordinal)
                ? new GitHubRepositoryPermissionProbe(id, owner, name, permission)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
