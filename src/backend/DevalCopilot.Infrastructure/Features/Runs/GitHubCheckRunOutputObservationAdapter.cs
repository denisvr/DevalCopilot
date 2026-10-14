using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCheckRunOutputObservation;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Observes, in two separate phases, the repository identity, the exact branch tip and one global node (a GitHub check run) for one
/// GitHub repository (ADR-0047) with one <c>gh api graphql --hostname github.com --method POST -f query=&lt;constant&gt;
/// -f owner=&lt;owner&gt; -f name=&lt;name&gt; -f ref=refs/heads/&lt;branch&gt; -f id=&lt;check run id&gt;</c> per phase, composed on the existing
/// <see cref="IProcessExecutionAdapter"/> inside the host scratch directory (never a repository, shell, jq or second process engine). The
/// metadata document selects only the check run's association (its repository, its check suite, the suite's repository and commit); the
/// output document repeats it and adds the name, status, conclusion, title, summary and text. Both are constants and read-only; the
/// stored names, the bounded owned branch and the identifier travel only as separate raw-string variables (<c>-f</c>, never <c>-F</c>), so
/// nothing is interpolated, coerced or re-parsed. The HTTP POST only transports the query. The adapter keeps no state between the phases
/// and never decides whether the second may run: the host admits that, calls each phase at most once per request, and retries nothing.
///
/// <para>
/// The child receives only a short allowlist of non-secret OS, profile and temp variables plus fixed controls that disable prompts, update
/// notifications, telemetry, color and spinners. No ambient token, host, config directory, debug, proxy, browser or pager override is
/// forwarded, so this is the stored default context only. This type never reads an auth file or credential; standard error is discarded
/// unread and raw provider output is never logged or retained. The metadata phase keeps the small 8 KiB and 16 KiB caps; only the output
/// phase captures up to 256 KiB per stream and 512 KiB combined, because the legal worst case (24 KiB of decoded text, every character
/// escaped as a six-byte sequence, plus the fixed envelope) is about 150 KiB. The other observers keep their own caps.
/// </para>
///
/// <para>
/// A singleton instance permits one in-flight invocation: a concurrent request is <see langword="null"/> immediately, with no queue,
/// retry or extra process, and the slot is released after every outcome. Only the whole exact JSON of the selected document is accepted,
/// at most eight levels deep, and every property name must be spelled literally: the raw token of each name is checked, because a decoded
/// name proves nothing about its spelling. The only strings that may carry JSON escapes are the check name and the title, summary and text
/// (decoded exactly once, held to their text contracts, redacted once more with the unchanged fixed redaction because the raw-text pass
/// cannot see an escaped character, and held to the contracts again), so an admitted value is safe text and not necessarily the
/// provider's original text; every other string must contain no backslash at all. GitHub's <c>qualifiedName</c> lookup can fall back to a
/// short name, so the prefix must be exactly <c>refs/heads/</c>, the name ordinally equal to the requested branch and the target a Commit
/// with a 40-character lowercase hexadecimal object ID. The node is explicit <see langword="null"/>, exactly the selected fields of a
/// check run echoing the requested identifier, or exactly one literal GraphQL type name. Errors, extensions, duplicate, missing or extra
/// properties at any level, unsupported enumerations, malformed or oversized values, a nonzero exit, a timeout, truncation, an
/// inaccessible repository and a response naming a different repository (compared ordinally, ignoring only ASCII case; never trimmed,
/// decoded or adopted after a rename or transfer) are <see langword="null"/>: neither nonexistence nor permission is inferred. Only the
/// caller's own cancellation and unexpected faults are raised.
/// </para>
/// </summary>
public sealed class GitHubCheckRunOutputObservationAdapter : IGitHubCheckRunOutputObserver
{
    internal const string MetadataQuery =
        "query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id "
        + "repository{id} checkSuite{id repository{id} commit{oid}}}}}";

    internal const string OutputQuery =
        "query($owner:String!,$name:String!,$ref:String!,$id:ID!){repository(owner:$owner,name:$name){id name nameWithOwner "
        + "ref(qualifiedName:$ref){name prefix target{__typename ... on Commit{oid}}}} node(id:$id){__typename ... on CheckRun{id "
        + "repository{id} checkSuite{id repository{id} commit{oid}} name status conclusion title summary text}}}";

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

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private static readonly JsonDocumentOptions StrictJson = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 8,
    };

    private static readonly JsonReaderOptions StrictReader = new()
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

    public GitHubCheckRunOutputObservationAdapter(IProcessExecutionAdapter processExecutionAdapter)
        : this(processExecutionAdapter, HostScratchDirectory.EnsureExists, System.Environment.GetEnvironmentVariable)
    {
    }

    internal GitHubCheckRunOutputObservationAdapter(
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

    public Task<GitHubCheckRunProbe?> ObserveMetadataAsync(
        string executablePath, string owner, string repository, string branchName, string checkRunId, CancellationToken cancellationToken) =>
        ObserveAsync(MetadataQuery, 8 * 1024, 16 * 1024, false, executablePath, owner, repository, branchName, checkRunId, cancellationToken);

    public Task<GitHubCheckRunProbe?> ObserveOutputAsync(
        string executablePath, string owner, string repository, string branchName, string checkRunId, CancellationToken cancellationToken) =>
        ObserveAsync(OutputQuery, 256 * 1024, 512 * 1024, true, executablePath, owner, repository, branchName, checkRunId, cancellationToken);

    private async Task<GitHubCheckRunProbe?> ObserveAsync(
        string query,
        int maxBytesPerStream,
        int maxTotalCapturedBytes,
        bool withOutput,
        string executablePath,
        string owner,
        string repository,
        string branchName,
        string checkRunId,
        CancellationToken cancellationToken)
    {
        if (!GitHubRepositoryNamePolicy.IsOwner(owner) || !GitHubRepositoryNamePolicy.IsRepository(repository)
            || !GitHubOwnedBranchNamePolicy.IsOwnedBranch(branchName) || !GitHubCheckRunOutputPolicy.IsCheckRunId(checkRunId))
        {
            return null;
        }

        if (!_probeSlot.Wait(0, CancellationToken.None))
        {
            return null;
        }

        try
        {
            return await ProbeAsync(
                query, maxBytesPerStream, maxTotalCapturedBytes, withOutput, executablePath, owner, repository, branchName, checkRunId,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _probeSlot.Release();
        }
    }

    private async Task<GitHubCheckRunProbe?> ProbeAsync(
        string query,
        int maxBytesPerStream,
        int maxTotalCapturedBytes,
        bool withOutput,
        string executablePath,
        string owner,
        string repository,
        string branchName,
        string checkRunId,
        CancellationToken cancellationToken)
    {
        var scratch = _scratchDirectory();
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = executablePath,
            Arguments =
            [
                "api", "graphql", "--hostname", "github.com", "--method", "POST",
                "-f", "query=" + query, "-f", "owner=" + owner, "-f", "name=" + repository, "-f", "ref=refs/heads/" + branchName,
                "-f", "id=" + checkRunId,
            ],
            WorkingDirectory = scratch,
            ApprovedRoot = scratch,
            EnvironmentVariables = BuildEnvironment(),
            Timeout = _timeout,
            MaxBytesPerStream = maxBytesPerStream,
            MaxTotalCapturedBytes = maxTotalCapturedBytes,
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

        var probe = Interpret(result.StandardOutput, branchName, withOutput);

        return probe is not null
            && string.Equals(probe.Owner, owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(probe.Repository, repository, StringComparison.OrdinalIgnoreCase)
            && GitHubCheckRunOutputPolicy.IsNode(probe.Node, checkRunId, withOutput)
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
    private static GitHubCheckRunProbe? Interpret(string standardOutput, string branchName, bool withOutput)
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
            if (data is null || data.Value.EnumerateObject().ToArray() is not { Length: 2 } dataProperties)
            {
                return null;
            }

            var repositories = dataProperties.Where(property => property.Name == "repository").ToArray();
            var nodes = dataProperties.Where(property => property.Name == "node").ToArray();
            if (repositories.Length != 1 || nodes.Length != 1 || repositories[0].Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var properties = repositories[0].Value.EnumerateObject().ToArray();
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

            GitHubCheckRunOutputRef? branch = null;
            if (refs[0].Value.ValueKind != JsonValueKind.Null)
            {
                branch = InterpretRef(refs[0].Value, branchName);
                if (branch is null)
                {
                    return null;
                }
            }

            var node = InterpretNode(nodes[0].Value, withOutput, out var nodeAdmitted);

            return nodeAdmitted ? new GitHubCheckRunProbe(id, owner, name, branch, node) : null;
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
    /// the prefix, the name and the target are each proved exactly.</summary>
    private static GitHubCheckRunOutputRef? InterpretRef(JsonElement reference, string branchName)
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
        if (target.Length != 2)
        {
            return null;
        }

        var typeName = Text(target, "__typename");
        var objectId = Text(target, "oid");

        return string.Equals(typeName, "Commit", StringComparison.Ordinal) && GitHubObservationTarget.IsCommitSha(objectId)
            ? new GitHubCheckRunOutputRef(name, objectId!)
            : null;
    }

    /// <summary>The node is an explicit null, exactly one literal type name (any type but a check run, with no other member) or exactly
    /// the selected fields of a check run. <paramref name="admitted"/> distinguishes a valid null from a refusal.</summary>
    private static GitHubCheckRunNode? InterpretNode(JsonElement element, bool withOutput, out bool admitted)
    {
        admitted = false;
        if (element.ValueKind == JsonValueKind.Null)
        {
            admitted = true;
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var properties = element.EnumerateObject().ToArray();
        var typeName = Text(properties, "__typename");
        if (typeName is null || !GitHubCheckRunOutputPolicy.IsTypeName(typeName))
        {
            return null;
        }

        if (!string.Equals(typeName, GitHubCheckRunOutputPolicy.CheckRunTypeName, StringComparison.Ordinal))
        {
            admitted = properties.Length == 1;
            return admitted ? new GitHubCheckRunNode(typeName, null, null) : null;
        }

        if (properties.Length != (withOutput ? 10 : 4))
        {
            return null;
        }

        var association = InterpretAssociation(properties);
        if (association is null)
        {
            return null;
        }

        if (!withOutput)
        {
            admitted = true;
            return new GitHubCheckRunNode(typeName, association, null);
        }

        var detail = InterpretDetail(properties);
        admitted = detail is not null;

        return detail is null ? null : new GitHubCheckRunNode(typeName, association, detail);
    }

    private static GitHubCheckRunAssociation? InterpretAssociation(JsonProperty[] properties)
    {
        var id = Text(properties, "id");
        var repositoryId = NestedId(properties, "repository", 1);
        var suites = properties.Where(property => property.Name == "checkSuite").ToArray();
        if (id is null || repositoryId is null || suites.Length != 1 || suites[0].Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var suite = suites[0].Value.EnumerateObject().ToArray();
        if (suite.Length != 3)
        {
            return null;
        }

        var suiteId = Text(suite, "id");
        var suiteRepositoryId = NestedId(suite, "repository", 1);
        var commits = suite.Where(property => property.Name == "commit").ToArray();
        if (suiteId is null || suiteRepositoryId is null || commits.Length != 1 || commits[0].Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var commit = commits[0].Value.EnumerateObject().ToArray();
        var oid = commit.Length == 1 ? Text(commit, "oid") : null;

        return oid is null ? null : new GitHubCheckRunAssociation(id, repositoryId, suiteId, suiteRepositoryId, oid);
    }

    /// <summary>The literal identifier of an object that has exactly the one member <c>id</c>.</summary>
    private static string? NestedId(JsonProperty[] properties, string name, int expectedMembers)
    {
        var matches = properties.Where(property => property.Name == name).ToArray();
        if (matches.Length != 1 || matches[0].Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var nested = matches[0].Value.EnumerateObject().ToArray();

        return nested.Length == expectedMembers ? Text(nested, "id") : null;
    }

    /// <summary>The check name, status, conclusion and the three output strings. Each decoded value is held to its contract, then
    /// redacted once with the unchanged internal redactor and held to the contract again; the trio is bounded together before and
    /// after redaction. An admitted value is therefore safe text, not necessarily the provider's original text.</summary>
    private static GitHubCheckRunDetail? InterpretDetail(JsonProperty[] properties)
    {
        var status = Text(properties, "status");
        var conclusions = properties.Where(property => property.Name == "conclusion").ToArray();
        if (status is null || conclusions.Length != 1 || !GitHubCheckRunOutputPolicy.IsStatus(status))
        {
            return null;
        }

        string? conclusion = null;
        if (conclusions[0].Value.ValueKind != JsonValueKind.Null)
        {
            conclusion = Text(conclusions, "conclusion");
            if (!GitHubCheckRunOutputPolicy.IsConclusion(conclusion))
            {
                return null;
            }
        }

        var names = Decoded(properties, "name", nullable: false);
        var titles = Decoded(properties, "title", nullable: true);
        var summaries = Decoded(properties, "summary", nullable: true);
        var texts = Decoded(properties, "text", nullable: true);
        if (!names.Ok || !titles.Ok || !summaries.Ok || !texts.Ok)
        {
            return null;
        }

        if (!GitHubCheckRunOutputPolicy.IsName(names.Value)
            || !GitHubCheckRunOutputPolicy.IsOutput(new GitHubCheckRunOutputText(titles.Value, summaries.Value, texts.Value)))
        {
            return null;
        }

        var name = Redact(names.Value) ?? string.Empty;
        var output = new GitHubCheckRunOutputText(Redact(titles.Value), Redact(summaries.Value), Redact(texts.Value));

        return GitHubCheckRunOutputPolicy.IsName(name) && GitHubCheckRunOutputPolicy.IsOutput(output)
            ? new GitHubCheckRunDetail(name, status, conclusion, output)
            : null;
    }

    /// <summary>A string property that occurs exactly once, with its JSON escapes decoded exactly once; an invalid surrogate sequence
    /// the decoder reports is a refusal, never an unhandled fault or a replacement character. A null is admitted only when the property
    /// is nullable.</summary>
    private static (bool Ok, string? Value) Decoded(JsonProperty[] properties, string name, bool nullable)
    {
        var matches = properties.Where(property => property.Name == name).ToArray();
        if (matches.Length != 1)
        {
            return (false, null);
        }

        if (matches[0].Value.ValueKind == JsonValueKind.Null)
        {
            return (nullable, null);
        }

        if (matches[0].Value.ValueKind != JsonValueKind.String)
        {
            return (false, null);
        }

        string? decoded;
        try
        {
            decoded = matches[0].Value.GetString();
        }
        catch (InvalidOperationException)
        {
            return (false, null);
        }

        return (decoded is not null, decoded);
    }

    /// <summary>The executor's fixed redaction ran on the raw JSON, where an escaped character is invisible to it, so the same unchanged
    /// <see cref="StreamingOutputRedactor"/> is applied once more to the decoded text. Empty text is returned as it is.</summary>
    private static string? Redact(string? decoded)
    {
        if (string.IsNullOrEmpty(decoded))
        {
            return decoded;
        }

        var redactor = new StreamingOutputRedactor();

        return Encoding.UTF8.GetString([.. redactor.ProcessChunk(Encoding.UTF8.GetBytes(decoded)), .. redactor.Flush()]);
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
}
