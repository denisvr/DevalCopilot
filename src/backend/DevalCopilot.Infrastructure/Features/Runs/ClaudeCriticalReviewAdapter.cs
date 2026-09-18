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
/// --prompt-suggestions false --tools "" --strict-mcp-config --permission-mode plan
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
/// <item><c>--prompt-suggestions false</c> — suppresses the provider's own predicted-next-prompt
/// side output; this attempt never has a next turn to suggest one for.</item>
/// <item><c>--permission-mode plan</c> — the strongest available permission mode: even if every
/// tool were somehow still reachable, this mode never executes an action, only plans one.</item>
/// </list>
/// <c>--bare</c> is deliberately never passed: the installed version's own <c>--help</c> states it
/// changes the authentication contract itself (Anthropic auth becomes strictly
/// <c>ANTHROPIC_API_KEY</c>/<c>apiKeyHelper</c>; OAuth and keychain are never read), which would
/// silently break this slice's reliance on existing local CLI authentication — isolation is
/// achieved entirely through the flags above instead. This adapter also never passes
/// <c>--continue</c>, <c>--resume</c>, <c>--fork-session</c>, <c>--dangerously-skip-permissions</c>,
/// <c>--allow-dangerously-skip-permissions</c>, <c>--mcp-config</c>, <c>--add-dir</c>, a model
/// flag, <c>--settings</c>, <c>--plugin-dir</c>/<c>--plugin-url</c>, <c>--agents</c>, a system
/// prompt override, or any other user- or repository-supplied argument. Never searches PATH or a
/// private desktop application layout — the launch target arrives already resolved and is only
/// revalidated here.
///
/// Unlike Codex, Claude's print-mode CLI has no file-based final-response flag: the schema-bound
/// result is nested in the <c>result</c> field of the single JSON envelope it writes to stdout.
/// This adapter extracts that field and writes it to the same
/// <see cref="ArtifactPurpose.AgentFinalResponse"/> sink Codex's own CLI writes to directly, so
/// every later stage (sealing, parsing, recording) is identical between both providers.
/// </summary>
public sealed class ClaudeCriticalReviewAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : ICriticalReviewAdapter
{
    private const int MaxContextManifestReadBytes = 32 * 1024;
    private const int MaxSessionIdLength = 256;

    public async Task<CriticalReviewInvocationResult> InvokeAsync(
        CriticalReviewInvocationRequest request, CancellationToken cancellationToken)
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
            "--json-schema", JsonSerializer.Serialize(ClaudeCriticalReviewOutputSchema.BuildSchemaDocument()),
            "--safe-mode",
            "--restricted",
            "--disable-slash-commands",
            "--no-chrome",
            "--permission-prompts", "none",
            "--prompt-suggestions", "false",
            "--tools", string.Empty,
            "--strict-mcp-config",
            "--permission-mode", "plan",
            "--no-session-persistence",
            "--session-id", Guid.NewGuid().ToString(),
            "--max-turns", "1",
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
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated);
        }

        var envelope = TryParseEnvelope(result.StandardOutput);
        if (envelope is null || envelope.IsError || envelope.FinalResponseJson is null)
        {
            // A non-zero exit is already handled above; this covers a zero exit that still
            // reported a business-level failure (e.g. hitting --max-turns) or an envelope this
            // adapter could not make sense of. Either way, there is no trustworthy final
            // response to seal as a proposal review — recorded as a truthful provider failure,
            // never as an empty or invented success.
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

        return new CriticalReviewInvocationResult(
            CriticalReviewInvocationOutcome.Exited,
            result.StandardOutputTruncated,
            result.StandardErrorTruncated,
            envelope.SessionId);
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

    /// <summary>
    /// Only non-secret OS/profile-location values required to locate existing local Claude Code
    /// authentication — never PATH, a provider token, or an arbitrary environment entry.
    /// <c>CLAUDE_CONFIG_DIR</c> is forwarded only when this host process already has one set;
    /// <c>USERPROFILE</c> is always forwarded so Claude Code can locate its default configuration
    /// directory when <c>CLAUDE_CONFIG_DIR</c> is not set. Mirrors
    /// <c>CodexPlanningAdapter.BuildEnvironmentAllowlist</c> exactly.
    /// </summary>
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

    /// <summary>
    /// Parses Claude's single <c>--print --output-format json</c> stdout envelope. Fails closed
    /// to <see langword="null"/> for anything not shaped as a JSON object with the expected
    /// fields — never partially trusts a malformed or ambiguous envelope, which the caller always
    /// maps to <c>ProviderInvocationFailed</c>, never to an invented success. Specifically:
    /// <c>is_error</c> must be present and a genuine JSON boolean — a missing or non-boolean
    /// value is an ambiguous shape this adapter never guesses about, and only a literal
    /// <see langword="false"/> is ever treated as success; <c>result</c> must be present (its
    /// absence is never treated as an empty or default response); and a <c>session_id</c>, when
    /// present and non-null, must be a well-shaped, bounded string — a present-but-malformed
    /// session id rejects the whole envelope rather than being silently dropped, since a provider
    /// that cannot even shape its own bookkeeping field correctly is not a source whose
    /// <c>result</c> should be trusted either. A genuine JSON <see langword="null"/> session id is
    /// tolerated exactly like an absent field (plausible here specifically because this attempt
    /// always passes <c>--no-session-persistence</c>, so the provider may have no session to
    /// report at all) — only a present, non-null value of the wrong shape is malformed. Any other
    /// field is tolerated and ignored: this is a fixed contract
    /// against a versioned CLI, not a strict schema over every field the provider may ever add.
    /// <c>result</c> is normalized to a bare JSON string regardless of whether the provider
    /// emitted it as a JSON string (containing schema-conformant JSON text) or as a
    /// schema-conformant JSON value directly; either way, the caller feeds the returned text into
    /// the same parser Codex's file-based final response goes through.
    /// </summary>
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
                // A genuine JSON null (plausible here specifically because this attempt always
                // passes --no-session-persistence, so the provider may have no session to report
                // at all) is treated exactly like the field being absent — tolerated, never
                // rejected. Only a present, non-null value that is not itself a well-shaped,
                // bounded string is treated as malformed.
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

    private static CriticalReviewInvocationResult Failed(bool standardOutputTruncated = false, bool standardErrorTruncated = false) =>
        new(CriticalReviewInvocationOutcome.Failed, standardOutputTruncated, standardErrorTruncated, ProviderSessionId: null);
}
