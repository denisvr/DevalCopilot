using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// Invokes the Claude Code CLI in bounded, fail-closed, read-only, non-interactive
/// critical-review mode using the locally observed contract:
/// <c>--print --input-format text --output-format json --json-schema &lt;inline schema&gt;
/// --safe-mode --restricted --disable-slash-commands --no-chrome --permission-prompts none
/// --tools "" --strict-mcp-config --permission-mode plan
/// --no-session-persistence --session-id &lt;fresh GUID&gt; --max-turns 1</c>, with the context
/// manifest written to stdin and stdin then closed.
///
/// <c>--tools ""</c> and <c>--strict-mcp-config</c> alone disable built-in tools and non-declared
/// MCP servers, but the installed CLI's own <c>--help</c> proves neither one disables hooks,
/// plugins, skills, CLAUDE.md auto-discovery, Claude-in-Chrome, or user/project/local settings
/// files — a <c>SessionStart</c> hook or a project-local setting could still cause a command to
/// run with no built-in tool ever invoked. Every argument below closes one of those gaps, each
/// justified directly from the installed version's own <c>--help</c> text:
/// <list type="bullet">
/// <item><c>--safe-mode</c> — disables CLAUDE.md, skills, plugins, hooks, MCP servers, custom
/// commands/agents, output styles, workflows, themes, and keybindings outright.</item>
/// <item><c>--restricted</c> — independently removes command/code-running tools and WebFetch, and
/// ignores user/project/local settings files (a second, independent path to the same
/// no-local-customization guarantee <c>--safe-mode</c> already provides).</item>
/// <item><c>--disable-slash-commands</c> — disables all skills (a slash command could otherwise
/// invoke project- or user-defined behavior).</item>
/// <item><c>--no-chrome</c> — disables the Claude-in-Chrome integration entirely; this attempt
/// never has any reason to control a browser.</item>
/// <item><c>--permission-prompts none</c> — anything that would otherwise prompt for permission is
/// denied automatically, independent of whatever the permission mode decides.</item>
/// <item><c>--permission-mode plan</c> — the strongest available permission mode: even if every
/// tool were somehow still reachable, this mode never executes an action, only plans one.</item>
/// </list>
/// <c>--bare</c> is deliberately never passed: the installed version's own <c>--help</c> states it
/// changes the authentication contract itself (Anthropic auth becomes strictly
/// <c>ANTHROPIC_API_KEY</c>/<c>apiKeyHelper</c>; OAuth and keychain are never read), which would
/// silently break this slice's reliance on existing local CLI authentication — isolation is
/// achieved entirely through the flags above instead. This adapter also never passes
/// <c>--continue</c>, <c>--resume</c>, <c>--fork-session</c>, <c>--dangerously-skip-permissions</c>,
/// <c>--allow-dangerously-skip-permissions</c>, <c>--mcp-config</c>, <c>--add-dir</c>, an unrequested model
/// flag (<c>--model &lt;alias&gt;</c> is passed only when the claimed attempt's immutable snapshot carries one
/// of the closed <see cref="ClaudeModelAlias"/> values, and <c>--effort &lt;level&gt;</c> only when it also carries a closed
/// <see cref="ClaudeEffortLevel"/> paired with sonnet or opus; see <see cref="ClaudeModelRequestArguments"/>),
/// <c>--settings</c>, <c>--plugin-dir</c>/<c>--plugin-url</c>, <c>--agents</c>, a system
/// prompt override, or any other user- or repository-supplied argument. Never searches PATH or a
/// private desktop application layout — the launch target arrives already resolved and is only
/// revalidated here.
///
/// Unlike Codex, Claude's print-mode CLI has no file-based final-response flag. ADR-0072 (contract
/// <c>claude-critical-review-v2</c>): the documented single JSON envelope on stdout carries the schema-bound
/// value in its <c>structured_output</c> field. The shared <see cref="ClaudeResultEnvelope"/> classifies the
/// process result and returns that object only for semantic success, never the narrative <c>result</c> text;
/// this adapter writes it to the same <see cref="ArtifactPurpose.AgentFinalResponse"/> sink Codex's own CLI
/// writes to directly, so sealing, schema parsing and recording stay identical between both providers. Every
/// failure carries one closed <see cref="AgentInvocationDiagnostic"/>. Only the current contract is
/// dispatched: an attempt claimed under the historical v1 contract fails closed before any process starts.
/// </summary>
public sealed class ClaudeCriticalReviewAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : ICriticalReviewAdapter
{
    private const int MaxContextManifestReadBytes = 32 * 1024;

    private readonly ClaudeProcessEnvironmentInputs _environmentInputs = ClaudeProcessEnvironmentInputs.Current;

    /// <summary>Test seam: the same adapter with controlled environment inputs, so its real control
    /// flow is exercised without reading a real process variable or the real system directory.</summary>
    internal ClaudeCriticalReviewAdapter(
        IProcessExecutionAdapter processExecutionAdapter,
        IArtifactStore artifactStore,
        ClaudeProcessEnvironmentInputs environmentInputs)
        : this(processExecutionAdapter, artifactStore)
    {
        _environmentInputs = environmentInputs;
    }

    public async Task<CriticalReviewInvocationResult> InvokeAsync(
        CriticalReviewInvocationRequest request, CancellationToken cancellationToken)
    {
        if (request.AdapterContractVersion != ReadOnlyFormatRepairPolicy.CriticalReviewAdapterContractVersion)
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

        // A cancelled request stays cancelled: the environment refusal below never masks it as a failure.
        cancellationToken.ThrowIfCancellationRequested();

        if (!ClaudeProcessEnvironment.TryBuild(_environmentInputs, out var environment))
        {
            // The Windows prerequisite could not be derived: refused before the manifest is read,
            // any sink is requested, any artifact is written or a process starts — never retried
            // with the smaller map.
            return Failed(AgentInvocationDiagnostic.EnvironmentUnavailable);
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
            return Failed(AgentInvocationDiagnostic.InvocationUnavailable);
        }

        var arguments = new List<string>
        {
            "--print",
            "--input-format", "text",
            "--output-format", "json",
            "--json-schema", JsonSerializer.Serialize(ClaudeCriticalReviewOutputSchema.BuildSchemaDocument()),
            "--safe-mode",
            "--restricted",
            "--disable-slash-commands",
            "--no-chrome",
            "--permission-prompts", "none",
            "--tools", string.Empty,
            "--strict-mcp-config",
            "--permission-mode", "plan",
            "--no-session-persistence",
            "--session-id", Guid.NewGuid().ToString(),
            "--max-turns", "1",
        };

        if (!ClaudeModelRequestArguments.TryAppend(
                arguments, request.RequestedClaudeModel, request.RequestedClaudeEffort))
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

        // Preserved for every real result — including a timeout, a cancellation, a non-zero exit,
        // incomplete I/O and a zero exit whose envelope is later rejected — never collapsed into the
        // closed Exited/Failed classification alone.
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

        return new CriticalReviewInvocationResult(
            CriticalReviewInvocationOutcome.Exited,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            reading.SessionId,
            ProcessEvidence: processEvidence,
            TokenUsage: reading.TokenUsage,
            ModelContextLimits: reading.ModelContextLimits);
    }

    /// <summary>Never a searched, invented, or PATH-resolved value — must already be a fully
    /// qualified path to an existing file, exactly what a previously-observed launch target's own
    /// component is stored as. Lexical containment and existence alone prove nothing once a
    /// junction or symbolic link sits anywhere between the filesystem root and the file, so every
    /// segment of <paramref name="path"/> — the leaf and every ancestor directory up to the root
    /// — is also revalidated here, immediately before process start, using the same
    /// segment-walking algorithm this application already established for capability discovery in
    /// <c>EnvironmentReadiness.PackageEntrypointResolver</c>. A rejection here fails the whole
    /// invocation closed; it never triggers a fallback search.</summary>
    private static bool IsAcceptableLaunchComponent(string path) =>
        Path.IsPathFullyQualified(path)
        && File.Exists(path)
        && !AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(path);

    /// <summary><paramref name="result"/> and <paramref name="processEvidence"/> are null only when no process result exists;
    /// <paramref name="tokenUsage"/> and <paramref name="modelLimits"/> come only from a structurally valid envelope of a zero exit
    /// with complete I/O, each judged independently.</summary>
    private static CriticalReviewInvocationResult Failed(
        AgentInvocationDiagnostic diagnostic,
        ProcessExecutionResult? result = null,
        AgentProcessEvidence? processEvidence = null,
        AgentTokenUsage? tokenUsage = null,
        AgentModelContextLimits? modelLimits = null) =>
        new(CriticalReviewInvocationOutcome.Failed, result?.StandardOutputTruncated ?? false, result?.StandardErrorTruncated ?? false,
            ProviderSessionId: null, processEvidence, tokenUsage, modelLimits, diagnostic);
}
