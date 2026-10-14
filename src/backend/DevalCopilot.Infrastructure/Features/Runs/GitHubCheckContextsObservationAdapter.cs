using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckContextsObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Observes the identity, the exact branch tip and the first bounded page (at most 50) of check and status contexts of that tip commit for
/// one GitHub repository (ADR-0045) with exactly one <c>gh api graphql --hostname github.com --method POST -f query=&lt;constant&gt;
/// -f owner=&lt;owner&gt; -f name=&lt;name&gt; -f ref=refs/heads/&lt;branch&gt;</c>, composed on the existing
/// <see cref="IProcessExecutionAdapter"/> inside the host scratch directory (never a repository, shell, jq or second process engine). The
/// GraphQL document is a constant read-only query; the stored names and the bounded owned branch travel only as separate raw-string
/// variables (<c>-f</c>, never <c>-F</c>), so nothing is interpolated, coerced or re-parsed. The HTTP POST only transports the query; no
/// mutation, other endpoint, pagination, cache or retry exists, and a collection larger than the bound is reported by its count alone.
///
/// <para>
/// The child receives only a short allowlist of non-secret OS, profile and temp variables plus fixed controls that disable prompts,
/// update notifications, telemetry, color and spinners. No ambient token, host, config directory, debug, proxy, browser or pager override
/// is forwarded, so this is the stored default context only. This type never reads an auth file or credential; standard error is
/// discarded unread. This is the only observer that captures up to 128 KiB per stream and 256 KiB combined, because the bounded
/// collection can legitimately be larger than a single aggregate; every other observer keeps its own smaller caps.
/// </para>
///
/// <para>
/// A singleton instance permits one in-flight observation: a concurrent request is <see langword="null"/> immediately, with no queue,
/// retry or extra process, and the slot is released after every outcome. Only the whole exact JSON of the selected document is accepted
/// and every property name must be spelled literally: the raw token of each name is checked, because a decoded name proves nothing about
/// its spelling. The only strings that may carry JSON escapes are the two label fields (<c>CheckRun.name</c> and
/// <c>StatusContext.context</c>), decoded exactly once, held to the label text contract, redacted once more with the unchanged fixed
/// redaction (the raw-text pass cannot see an escaped character) and held to the contract again, so an admitted label is safe text and
/// not necessarily the provider's original text; every other string must contain no backslash at all. GitHub's <c>qualifiedName</c> lookup can fall back to a short name, so the prefix must be exactly
/// <c>refs/heads/</c>, the name ordinally equal to the requested branch and the target a Commit with a 40-character lowercase hexadecimal
/// object ID. The connection must be exactly <c>{totalCount, pageInfo{hasNextPage}, nodes}</c> with a non-negative Int32 count, a page of
/// exactly the lesser of 50 and that count, and a page flag equal to whether the count exceeds 50. Errors, extensions, duplicate, missing
/// or extra properties at any level, null or partial entries, unknown typenames, unsupported enumerations, a duplicate entry identifier,
/// malformed or oversized values, a nonzero exit, a timeout, truncation, an inaccessible repository and a response naming a different
/// repository (compared ordinally, ignoring only ASCII case; never trimmed, decoded or adopted after a rename or transfer) are
/// <see langword="null"/>: neither nonexistence nor permission is inferred, a valid null ref means only that GitHub returned no ref and a
/// valid null rollup only that it returned no rollup. Only the caller's own cancellation and unexpected faults are raised.
/// </para>
/// </summary>
public sealed class GitHubCheckContextsObservationAdapter : IGitHubCheckContextsObserver
{
    internal const string Query =
        "query($owner:String!,$name:String!,$ref:String!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid statusCheckRollup{contexts(first:50)"
        + "{totalCount pageInfo{hasNextPage} nodes{__typename ... on CheckRun{id name status conclusion checkSuite{id}} "
        + "... on StatusContext{id context state}}}}}}}}}";

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

    private const int MaxBytesPerStream = 128 * 1024;
    private const int MaxTotalCapturedBytes = 256 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonDocumentOptions StrictJson = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 12,
    };

    private static readonly JsonReaderOptions StrictReader = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 12,
    };

    private readonly IProcessExecutionAdapter _processExecutionAdapter;
    private readonly Func<string> _scratchDirectory;
    private readonly Func<string, string?> _readEnvironmentVariable;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _probeSlot = new(1, 1);

    public GitHubCheckContextsObservationAdapter(IProcessExecutionAdapter processExecutionAdapter)
        : this(processExecutionAdapter, HostScratchDirectory.EnsureExists, System.Environment.GetEnvironmentVariable)
    {
    }

    internal GitHubCheckContextsObservationAdapter(
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

    public async Task<GitHubCheckContextsProbe?> ObserveAsync(
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

    private async Task<GitHubCheckContextsProbe?> ProbeAsync(
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

    /// <summary>Only the exact shape is accepted; null is unknown. Duplicate properties are rejected, not merged, and an escaped property
    /// name is rejected on its raw token because the decoded name cannot prove how it was spelled.</summary>
    private static GitHubCheckContextsProbe? Interpret(string standardOutput, string branchName)
    {
        var bytes = Encoding.UTF8.GetBytes(standardOutput);
        try
        {
            if (HasEscapedPropertyName(bytes))
            {
                return null;
            }

            using var document = JsonDocument.Parse(bytes, StrictJson);
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
                return new GitHubCheckContextsProbe(id, owner, name, null, null);
            }

            var branch = InterpretRef(refs[0].Value, branchName);

            return branch is null ? null : new GitHubCheckContextsProbe(id, owner, name, branch.Value.Ref, branch.Value.Page);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A property name that was written with an escape is refused wherever it occurs. The same pass proves the whole text is one
    /// well-formed JSON value within the depth bound; a malformed escape or an unterminated value is a <see cref="JsonException"/>.</summary>
    private static bool HasEscapedPropertyName(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, StrictReader);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueIsEscaped)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The ref is the recorded branch head commit or nothing: GitHub's qualified-name lookup can fall back to other names, so
    /// the prefix, the name, the target and the rollup are each proved exactly.</summary>
    private static (GitHubCheckContextsRef Ref, GitHubCheckContextsPage? Page)? InterpretRef(JsonElement reference, string branchName)
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

        var branchRef = new GitHubCheckContextsRef(name, objectId!);
        if (rollups[0].Value.ValueKind == JsonValueKind.Null)
        {
            return (branchRef, null);
        }

        var page = InterpretRollup(rollups[0].Value);

        return page is null ? null : (branchRef, page);
    }

    private static GitHubCheckContextsPage? InterpretRollup(JsonElement rollup)
    {
        if (rollup.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var rollupProperties = rollup.EnumerateObject().ToArray();
        var connections = rollupProperties.Where(property => property.Name == "contexts").ToArray();
        if (rollupProperties.Length != 1 || connections.Length != 1 || connections[0].Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var connection = connections[0].Value.EnumerateObject().ToArray();
        if (connection.Length != 3)
        {
            return null;
        }

        var totals = connection.Where(property => property.Name == "totalCount").ToArray();
        var pageInfos = connection.Where(property => property.Name == "pageInfo").ToArray();
        var nodes = connection.Where(property => property.Name == "nodes").ToArray();
        if (totals.Length != 1 || pageInfos.Length != 1 || nodes.Length != 1 || nodes[0].Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var total = PlainCount(totals[0].Value);
        var hasNextPage = HasNextPage(pageInfos[0].Value);
        if (total is null || hasNextPage is null || hasNextPage.Value != (total.Value > GitHubCheckContextsPolicy.MaxContexts))
        {
            return null;
        }

        var contexts = new List<GitHubCheckContext>();
        foreach (var node in nodes[0].Value.EnumerateArray())
        {
            var context = InterpretNode(node);
            if (context is null)
            {
                return null;
            }

            contexts.Add(context);
        }

        var page = new GitHubCheckContextsPage(total.Value, contexts);

        return GitHubCheckContextsPolicy.IsPage(page) ? page : null;
    }

    /// <summary>A non-negative Int32 written as plain digits: no sign, fraction, exponent, leading zero, string or null.</summary>
    private static int? PlainCount(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var raw = element.GetRawText();
        var plain = raw.Length > 0 && raw.All(character => character is >= '0' and <= '9') && (raw.Length == 1 || raw[0] != '0');

        return plain && element.TryGetInt32(out var value) && value >= 0 ? value : null;
    }

    private static bool? HasNextPage(JsonElement pageInfo)
    {
        if (pageInfo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = pageInfo.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != "hasNextPage")
        {
            return null;
        }

        return properties[0].Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>One entry of exactly the selected fields of its one known typename. Only the label is decoded; everything else is literal.
    /// The page-level policy then validates every value, including the closed enumerations and the identifiers.</summary>
    private static GitHubCheckContext? InterpretNode(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = node.EnumerateObject().ToArray();
        var typeName = Text(properties, "__typename");
        if (string.Equals(typeName, GitHubCheckContextsPolicy.CheckRunKind, StringComparison.Ordinal) && properties.Length == 6)
        {
            var id = Text(properties, "id");
            var label = Label(properties, "name");
            var status = Text(properties, "status");
            var suites = properties.Where(property => property.Name == "checkSuite").ToArray();
            var conclusions = properties.Where(property => property.Name == "conclusion").ToArray();
            if (id is null || label is null || status is null || suites.Length != 1 || conclusions.Length != 1
                || suites[0].Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var suiteProperties = suites[0].Value.EnumerateObject().ToArray();
            var suite = suiteProperties.Length == 1 ? Text(suiteProperties, "id") : null;
            string? conclusion = null;
            if (conclusions[0].Value.ValueKind != JsonValueKind.Null)
            {
                conclusion = Text(conclusions, "conclusion");
                if (conclusion is null)
                {
                    return null;
                }
            }

            return suite is null ? null : new GitHubCheckContext(typeName!, id, label, status, conclusion, suite);
        }

        if (string.Equals(typeName, GitHubCheckContextsPolicy.StatusContextKind, StringComparison.Ordinal) && properties.Length == 4)
        {
            var id = Text(properties, "id");
            var label = Label(properties, "context");
            var state = Text(properties, "state");

            return id is null || label is null || state is null
                ? null
                : new GitHubCheckContext(typeName!, id, label, state, null, null);
        }

        return null;
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

    /// <summary>A string property that occurs exactly once and carries no JSON escape (so nothing is decoded). The raw text is checked
    /// first, so an invalid escape can never reach the decoder.</summary>
    private static string? Text(JsonProperty[] properties, string name)
    {
        var matches = properties.Where(property => property.Name == name).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String
            || matches[0].Value.GetRawText().Contains('\\', StringComparison.Ordinal))
        {
            return null;
        }

        return matches[0].Value.GetString();
    }

    /// <summary>A label property that occurs exactly once. Its JSON escapes are decoded exactly once (the invalid surrogate sequences the
    /// decoder reports are a refusal, never an unhandled fault or a replacement character) and the decoded text is held to the bounded
    /// label contract. The executor's fixed redaction ran on the raw JSON, where an escaped character is invisible to it, so the same
    /// unchanged <see cref="StreamingOutputRedactor"/> is applied once more to the decoded text, and only the redacted text, held to the
    /// label contract again, is exposed. An admitted label is therefore safe text, not necessarily the provider's original text.</summary>
    private static string? Label(JsonProperty[] properties, string name)
    {
        var matches = properties.Where(property => property.Name == name).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? decoded;
        try
        {
            decoded = matches[0].Value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (!GitHubCheckContextsPolicy.IsLabel(decoded))
        {
            return null;
        }

        var redactor = new StreamingOutputRedactor();
        var redacted = Encoding.UTF8.GetString([.. redactor.ProcessChunk(Encoding.UTF8.GetBytes(decoded!)), .. redactor.Flush()]);

        return GitHubCheckContextsPolicy.IsLabel(redacted) ? redacted : null;
    }
}
