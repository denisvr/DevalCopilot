using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Invokes the Claude Code CLI in bounded, fail-closed, non-interactive implementation mode using
/// the locally observed contract:
/// <c>--print --input-format text --output-format json --json-schema &lt;inline schema&gt;
/// --safe-mode --restricted --disable-slash-commands --no-chrome --permission-prompts none
/// --prompt-suggestions false --tools "Read,Edit,Write,Glob,Grep" --strict-mcp-config
/// --permission-mode acceptEdits --no-session-persistence --session-id &lt;fresh GUID&gt;</c>, with
/// the context manifest written to stdin and stdin then closed. A dedicated adapter, never the
/// critical-review adapter reused by changing flags: this role's tool allowlist and permission
/// mode differ in kind (mutating repository edits) from every other Claude usage in this
/// protocol, so its own copy of every hardening decision below is deliberate, not incidental
/// duplication.
///
/// Differences from <see cref="ClaudeCriticalReviewAdapter"/>, each deliberate:
/// <list type="bullet">
/// <item><c>--tools "Read,Edit,Write,Glob,Grep"</c> — an explicit allowlist, not the empty
/// allowlist critical review uses: this role must actually read and edit repository files. Every
/// name is confirmed present as a literal, capitalized tool identifier in the installed CLI
/// binary. Bash, WebFetch, WebSearch, NotebookEdit, Task/Agent, and ExitPlanMode are all
/// deliberately absent — this attempt never runs a shell, code, or arbitrary process, never
/// browses the web, and never spawns a sub-agent.</item>
/// <item><c>--permission-mode acceptEdits</c> — the plan mode critical review uses would never
/// let this role actually edit anything; <c>bypassPermissions</c> is explicitly refused by
/// <c>--restricted</c>, so this is the strongest mode still compatible with real file mutation.
/// This is the one genuinely inferential point in this contract: the installed CLI's own
/// <c>--help</c> only names the enum value and does not document its exact runtime semantics
/// beyond the name and the accompanying embedded-binary strings ("auto-accept edits",
/// "acceptEditsSuggestionApplies") — disclosed here as a documented limitation, not asserted as
/// directly observed.</item>
/// <item>No <c>--max-turns</c> — unlike a single-turn critical review, implementation is
/// inherently multi-step (read, edit, re-read); the actual bound remains the process-level
/// <see cref="ProcessExecutionRequest.Timeout"/> plus cancellation and process-tree
/// termination, mirroring Codex planning's own unbounded-turn design.</item>
/// </list>
/// Every other hardening decision — <c>--safe-mode</c>, <c>--restricted</c>,
/// <c>--disable-slash-commands</c>, <c>--no-chrome</c>, <c>--permission-prompts none</c>,
/// <c>--prompt-suggestions false</c>, <c>--strict-mcp-config</c>, <c>--no-session-persistence</c>,
/// never <c>--bare</c>/<c>--continue</c>/<c>--resume</c>/<c>--fork-session</c>/
/// <c>--dangerously-skip-permissions</c>/<c>--mcp-config</c>/<c>--add-dir</c>/a model flag/
/// <c>--settings</c>/<c>--plugin-dir</c>/<c>--plugin-url</c>/<c>--agents</c>/a system prompt
/// override — is identical to <see cref="ClaudeCriticalReviewAdapter"/> and justified there. This
/// adapter never runs Git, a verification command, a commit, a push, a package install, or any
/// network operation on Claude's behalf: the tool allowlist above is the only capability granted,
/// and none of those actions are reachable through it.
/// </summary>
public sealed class ClaudeImplementationAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : IClaudeImplementationAdapter
{
    private const int MaxContextManifestReadBytes = 32 * 1024;
    private const int MaxSessionIdLength = 256;

    public async Task<ImplementationInvocationResult> InvokeAsync(
        ImplementationInvocationRequest request, CancellationToken cancellationToken)
    {
        if (!IsAcceptableLaunchComponent(request.LaunchExecutablePath))
        {
            // Revalidation only — never a new search through PATH or a private install
            // location. A component that no longer exists or no longer has its accepted shape
            // fails this invocation closed before any process starts.
            return Failed();
        }

        var manifestWindow = await artifactStore.VerifyAndReadSealedAsync(
            request.ContextManifestRelativeStoragePath,
            request.ContextManifestByteLength,
            request.ContextManifestContentHash,
            fromOffset: 0,
            maxBytes: MaxContextManifestReadBytes,
            cancellationToken).ConfigureAwait(false);
        if (manifestWindow.Status != SealedReadStatus.Ok)
        {
            return Failed();
        }

        var arguments = new List<string>
        {
            "--print",
            "--input-format", "text",
            "--output-format", "json",
            "--json-schema", JsonSerializer.Serialize(ImplementationReportOutputSchema.BuildSchemaDocument()),
            "--safe-mode",
            "--restricted",
            "--disable-slash-commands",
            "--no-chrome",
            "--permission-prompts", "none",
            "--prompt-suggestions", "false",
            "--tools", "Read,Edit,Write,Glob,Grep",
            "--strict-mcp-config",
            "--permission-mode", "acceptEdits",
            "--no-session-persistence",
            "--session-id", Guid.NewGuid().ToString(),
        };

        var executionRequest = new ProcessExecutionRequest
        {
            ExecutablePath = request.LaunchExecutablePath,
            Arguments = arguments,
            WorkingDirectory = request.WorkspacePath,
            ApprovedRoot = request.WorkspacePath,
            EnvironmentVariables = BuildEnvironmentAllowlist(),
            Timeout = request.Timeout,
            MaxBytesPerStream = request.MaxBytesPerStream,
            MaxTotalCapturedBytes = request.MaxTotalCapturedBytes,
            StandardOutputSinkPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentStandardOutput),
            StandardErrorSinkPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentStandardError),
            StandardInput = Encoding.UTF8.GetBytes(manifestWindow.Text),
        };

        ProcessExecutionResult result;
        try
        {
            result = await processExecutionAdapter.ExecuteAsync(executionRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed();
        }

        if (result.Outcome != ProcessExecutionOutcome.Exited || result.ExitCode != 0)
        {
            // Deliberately never short-circuits without capturing whatever partial
            // stdout/stderr the process produced — the caller always independently re-reads
            // fresh Git evidence after this returns, regardless of this outcome, since the
            // worktree may already have been mutated before this failure occurred.
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated);
        }

        var envelope = TryParseEnvelope(result.StandardOutput);
        if (envelope is null || envelope.IsError || envelope.FinalResponseJson is null)
        {
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated);
        }

        try
        {
            var resultPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            await File.WriteAllTextAsync(resultPath, envelope.FinalResponseJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated);
        }

        return new ImplementationInvocationResult(
            ImplementationInvocationOutcome.Exited,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            envelope.SessionId);
    }

    /// <summary>Mirrors <c>ClaudeCriticalReviewAdapter.IsAcceptableLaunchComponent</c>
    /// exactly.</summary>
    private static bool IsAcceptableLaunchComponent(string path) =>
        Path.IsPathFullyQualified(path)
        && File.Exists(path)
        && !AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(path);

    /// <summary>Mirrors <c>ClaudeCriticalReviewAdapter.BuildEnvironmentAllowlist</c>
    /// exactly.</summary>
    private static Dictionary<string, string> BuildEnvironmentAllowlist()
    {
        var environment = new Dictionary<string, string>();

        var userProfile = System.Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(userProfile))
        {
            environment["USERPROFILE"] = userProfile;
        }

        var claudeConfigDir = System.Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrEmpty(claudeConfigDir))
        {
            environment["CLAUDE_CONFIG_DIR"] = claudeConfigDir;
        }

        return environment;
    }

    /// <summary>Mirrors <c>ClaudeCriticalReviewAdapter.TryParseEnvelope</c> exactly — its own
    /// copy, not a shared helper, per this slice's explicit instruction not to reuse the
    /// critical-review adapter.</summary>
    private static ClaudeEnvelope? TryParseEnvelope(string standardOutput)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(standardOutput);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("is_error", out var isErrorElement)
                || (isErrorElement.ValueKind != JsonValueKind.True && isErrorElement.ValueKind != JsonValueKind.False))
            {
                return null;
            }

            var isError = isErrorElement.ValueKind == JsonValueKind.True;

            if (!root.TryGetProperty("result", out var resultElement))
            {
                return null;
            }

            var finalResponseJson = resultElement.ValueKind == JsonValueKind.String
                ? resultElement.GetString()
                : resultElement.GetRawText();

            string? sessionId = null;
            if (root.TryGetProperty("session_id", out var sessionIdElement) && sessionIdElement.ValueKind != JsonValueKind.Null)
            {
                if (sessionIdElement.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                var value = sessionIdElement.GetString();
                if (string.IsNullOrWhiteSpace(value) || value.Length > MaxSessionIdLength)
                {
                    return null;
                }

                sessionId = value;
            }

            return new ClaudeEnvelope(isError, finalResponseJson, sessionId);
        }
    }

    private sealed record ClaudeEnvelope(bool IsError, string? FinalResponseJson, string? SessionId);

    private static ImplementationInvocationResult Failed(bool standardOutputTruncated = false, bool standardErrorTruncated = false) =>
        new(ImplementationInvocationOutcome.Failed, standardOutputTruncated, standardErrorTruncated, ProviderSessionId: null);
}
