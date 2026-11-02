using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Invokes the Claude Code CLI in bounded, fail-closed, non-interactive implementation mode using
/// the locally observed contract:
/// <c>--print --input-format text --output-format json --json-schema &lt;inline schema&gt;
/// --safe-mode --restricted --disable-slash-commands --no-chrome --permission-prompts none
/// --tools "Read,Edit,Write,Glob,Grep" --strict-mcp-config
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
/// <item>No <c>--max-turns</c> unless the attempt's immutable snapshot requested one — unlike a
/// single-turn critical review, implementation is inherently multi-step (read, edit, re-read). An
/// optional owner request for the documented print-mode agentic-turn limit is passed exactly once as
/// <c>--max-turns N</c> (see <see cref="ClaudeMutationTurnLimitArguments"/>); without one the only
/// bound remains the process-level <see cref="ProcessExecutionRequest.Timeout"/> plus cancellation and
/// process-tree termination. The limit is a provider-loop request, never a measured turn count or a
/// token, cost, or account ceiling, and a provider error on reaching it is an ordinary failed
/// invocation.</item>
/// </list>
/// Every other hardening decision — <c>--safe-mode</c>, <c>--restricted</c>,
/// <c>--disable-slash-commands</c>, <c>--no-chrome</c>, <c>--permission-prompts none</c>,
/// <c>--strict-mcp-config</c>, <c>--no-session-persistence</c>,
/// never <c>--bare</c>/<c>--continue</c>/<c>--resume</c>/<c>--fork-session</c>/
/// <c>--dangerously-skip-permissions</c>/<c>--mcp-config</c>/<c>--add-dir</c>/an unrequested model flag/
/// <c>--settings</c>/<c>--plugin-dir</c>/<c>--plugin-url</c>/<c>--agents</c>/a system prompt
/// override — is identical to <see cref="ClaudeCriticalReviewAdapter"/> and justified there. This
/// adapter never runs Git, a verification command, a commit, a push, a package install, or any
/// network operation on Claude's behalf: the tool allowlist above is the only capability granted,
/// and none of those actions are reachable through it.
///
/// ADR-0072 (contract <c>claude-implementation-v3</c>): only the current contract is dispatched, and the shared
/// <see cref="ClaudeResultEnvelope"/> returns the schema-bound <c>structured_output</c> object only for semantic
/// success, never the narrative <c>result</c> text; every failure carries one closed
/// <see cref="AgentInvocationDiagnostic"/>.
/// </summary>
public sealed class ClaudeImplementationAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : IClaudeImplementationAdapter
{
    private const int MaxContextManifestReadBytes = 32 * 1024;

    private readonly ClaudeProcessEnvironmentInputs _environmentInputs = ClaudeProcessEnvironmentInputs.Current;

    /// <summary>Test seam: the same adapter with controlled environment inputs, so its real control
    /// flow is exercised without reading a real process variable or the real system directory.</summary>
    internal ClaudeImplementationAdapter(
        IProcessExecutionAdapter processExecutionAdapter,
        IArtifactStore artifactStore,
        ClaudeProcessEnvironmentInputs environmentInputs)
        : this(processExecutionAdapter, artifactStore)
    {
        _environmentInputs = environmentInputs;
    }

    public async Task<ImplementationInvocationResult> InvokeAsync(
        ImplementationInvocationRequest request, CancellationToken cancellationToken)
    {
        if (!ClaudeMutationAdapterContract.IsCurrentVersionFor(AgentResponseContract.ImplementationReport, request.AdapterContractVersion))
        {
            // ADR-0072: only the current contract is dispatched; a historical attempt is never upgraded or redispatched.
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        if (!IsAcceptableLaunchComponent(request.LaunchExecutablePath))
        {
            // Revalidation only — never a new search through PATH or a private install
            // location. A component that no longer exists or no longer has its accepted shape
            // fails this invocation closed before any process starts.
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        // Mirrors ClaudeCriticalReviewAdapter: a cancelled request is never masked as an environment refusal.
        cancellationToken.ThrowIfCancellationRequested();

        if (!ClaudeProcessEnvironment.TryBuild(_environmentInputs, out var environment))
        {
            // Mirrors ClaudeCriticalReviewAdapter: refused before the manifest is read, any sink is
            // requested, any artifact is written or a process starts — never retried with the smaller map.
            return Failed(AgentInvocationDiagnostic.EnvironmentUnavailable);
        }

        var manifestWindow = await artifactStore.VerifyAndReadSealedAsync(
            request.ContextManifestRelativeStoragePath,
            request.ContextManifestByteLength,
            request.ContextManifestContentHash,
            fromOffset: 0,
            maxBytes: MaxContextManifestReadBytes,
            cancellationToken).ConfigureAwait(false);
        // The sealed manifest must agree exactly with the attempt's immutable direct-guidance snapshot carried by the
        // request; any disagreement fails closed here, before any process starts, and never reconstructs context.
        if (manifestWindow.Status != SealedReadStatus.Ok
            || !DirectHumanGuidanceManifest.Agrees(manifestWindow.Text, request.DirectHumanGuidance))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        // The same agreement for the human plan authorization (ADR-0016): a sealed manifest that carries the authorized
        // form must be expected by the attempt's durable facts, with exactly those identifiers and rationale, and an
        // ordinary attempt's manifest must carry none. No process starts on a disagreement and nothing is reconstructed.
        if (!PlanningImplementationAuthorizationManifest.Agrees(manifestWindow.Text, request.PlanningAuthorization))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
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
            "--tools", "Read,Edit,Write,Glob,Grep",
            "--strict-mcp-config",
            "--permission-mode", "acceptEdits",
            "--no-session-persistence",
            "--session-id", Guid.NewGuid().ToString(),
        };

        if (!ClaudeModelRequestArguments.TryAppend(
                arguments, request.RequestedClaudeModel, request.RequestedClaudeEffort))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        if (!ClaudeMutationTurnLimitArguments.TryAppend(
                arguments, request.RequestedMaxTurns, request.AdapterContractVersion,
                ClaudeMutationAdapterContract.CurrentImplementation))
        {
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        var executionRequest = new ProcessExecutionRequest
        {
            ExecutablePath = request.LaunchExecutablePath,
            Arguments = arguments,
            WorkingDirectory = request.WorkspacePath,
            ApprovedRoot = request.WorkspacePath,
            EnvironmentVariables = environment,
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
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        // Preserved for every real result, never collapsed into the closed Exited/Failed
        // classification alone — mirrors ClaudeCriticalReviewAdapter. A failure deliberately never
        // short-circuits the caller's own fresh Git evidence read: the worktree may already have been
        // mutated before this failure occurred.
        var processEvidence = AgentProcessEvidence.FromProcessExecutionResult(result);
        var reading = ClaudeResultEnvelope.Classify(result);
        if (!reading.IsSuccess)
        {
            return Failed(reading.Diagnostic!.Value, result, processEvidence, reading.TokenUsage, reading.ModelContextLimits);
        }

        try
        {
            var resultPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            await File.WriteAllTextAsync(resultPath, reading.StructuredOutputJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(
                AgentInvocationDiagnostic.CaptureFault, result, processEvidence, reading.TokenUsage, reading.ModelContextLimits);
        }

        return new ImplementationInvocationResult(
            ImplementationInvocationOutcome.Exited,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            reading.SessionId,
            ProcessEvidence: processEvidence,
            TokenUsage: reading.TokenUsage,
            ModelContextLimits: reading.ModelContextLimits);
    }

    /// <summary>Mirrors <c>ClaudeCriticalReviewAdapter.IsAcceptableLaunchComponent</c>
    /// exactly.</summary>
    private static bool IsAcceptableLaunchComponent(string path) =>
        Path.IsPathFullyQualified(path)
        && File.Exists(path)
        && !AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(path);

    /// <summary>Mirrors <c>ClaudeCriticalReviewAdapter.Failed</c>.</summary>
    private static ImplementationInvocationResult Failed(
        AgentInvocationDiagnostic diagnostic,
        ProcessExecutionResult? result = null,
        AgentProcessEvidence? processEvidence = null,
        AgentTokenUsage? tokenUsage = null,
        AgentModelContextLimits? modelLimits = null) =>
        new(ImplementationInvocationOutcome.Failed, result?.StandardOutputTruncated ?? false, result?.StandardErrorTruncated ?? false,
            ProviderSessionId: null, ProcessEvidence: processEvidence, TokenUsage: tokenUsage, ModelContextLimits: modelLimits,
            Diagnostic: diagnostic);
}
