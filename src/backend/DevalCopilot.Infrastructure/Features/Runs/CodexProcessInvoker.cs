using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The one shared, bounded Codex CLI invocation contract every Codex adapter in this application
/// reuses: <c>exec --json --output-schema &lt;file&gt; --output-last-message &lt;file&gt; --sandbox
/// read-only --cd &lt;workspace&gt; --ephemeral --ignore-user-config [--model &lt;id&gt;] [--config
/// model_reasoning_effort=&lt;effort&gt;] -</c>, with the caller's context manifest written to stdin
/// and stdin then closed. The bracketed <c>--model</c>/<c>--config</c> pair is the sole, narrow
/// exception to this contract's otherwise-fixed argument list: each is added only when the
/// caller's already-claimed, already-validated <see cref="Request.RequestedModel"/>/
/// <see cref="Request.RequestedEffort"/> is non-null, and each value is independently revalidated
/// against the same bounded, safe identifier character set as every other assignment identifier
/// in this application immediately before it is ever placed on the command line — never a raw,
/// unchecked pass-through. Still never passes
/// <c>--dangerously-bypass-approvals-and-sandbox</c>, workspace-write/full-access, hooks, worktree
/// creation, <c>add-dir</c>, an arbitrary config override, or any other user-supplied argument.
/// Never searches PATH or a private desktop application layout — the launch target arrives
/// already resolved and is only revalidated here. Extracted out of the original
/// <c>CodexPlanningAdapter</c> so a second Codex role never has to copy this contract; only the
/// output-schema document, the artifact-purpose-scoped sink paths, and the optional requested
/// model/effort vary by caller.
/// </summary>
internal static class CodexProcessInvoker
{
    internal const int MaxContextManifestReadBytes = 32 * 1024;
    private const int MaxSessionIdScanBytes = 4 * 1024;
    private const int MaxSessionIdLength = 256;

    /// <summary>Mirrors the bounded assignment-identifier length every other Codex/Claude
    /// assignment field in this application already trusts (see <c>Attempt</c>'s own
    /// <c>ValidateAssignmentIdentifier</c>).</summary>
    private const int MaxAssignmentIdentifierLength = 128;

    internal sealed record Request(
        Guid RunId,
        Guid AttemptId,
        string WorkspacePath,
        string ContextManifestRelativeStoragePath,
        long ContextManifestByteLength,
        string ContextManifestContentHash,
        string LaunchExecutablePath,
        string? LaunchScriptPath,
        TimeSpan Timeout,
        int MaxBytesPerStream,
        int MaxTotalCapturedBytes,
        object OutputSchemaDocument,
        string? RequestedModel = null,
        string? RequestedEffort = null);

    /// <summary>The invocation result. <paramref name="ProcessEvidence"/> is set whenever the
    /// process adapter returned a real result — including a timeout, cancellation, or non-zero
    /// exit — and is null only when no process result exists at all (<see cref="Failed"/>), so a
    /// caller can never mistake a pre-start failure for a measured one.</summary>
    internal sealed record Outcome(
        bool Succeeded,
        bool StandardOutputTruncated,
        bool StandardErrorTruncated,
        string? ProviderSessionId,
        AgentProcessEvidence? ProcessEvidence,
        AgentTokenUsage? TokenUsage = null)
    {
        internal static readonly Outcome Failed = new(false, false, false, null, null);
    }

    internal static async Task<Outcome> InvokeAsync(
        IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore, Request request, CancellationToken cancellationToken)
    {
        if (!IsAcceptableLaunchComponent(request.LaunchExecutablePath)
            || (request.LaunchScriptPath is not null && !IsAcceptableLaunchComponent(request.LaunchScriptPath)))
        {
            // Revalidation only — never a new search through PATH or a private install
            // location. A component that no longer exists or no longer has its accepted shape
            // fails this invocation closed before any process starts.
            return Outcome.Failed;
        }

        if (!IsAcceptableAssignmentValue(request.RequestedModel) || !IsAcceptableAssignmentValue(request.RequestedEffort))
        {
            // Revalidation only, mirroring the launch-target check above: every requested model
            // and effort was already validated at claim time (and, before that, against a live
            // catalog observation), but this invocation never trusts that chain alone — a value
            // that somehow no longer has its accepted shape fails the whole invocation closed
            // rather than silently falling back to the CLI's own default.
            return Outcome.Failed;
        }

        if (request.RequestedModel is null && request.RequestedEffort is not null)
        {
            // Mirrors Attempt's own claim-time invariant (an effort is never accepted without a
            // model) as a second, independent boundary: this invocation never trusts that the
            // claimed attempt it was given actually enforced it. An effort-only request would
            // silently apply the CLI's own default model with an overridden effort — never
            // launched.
            return Outcome.Failed;
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
            return Outcome.Failed;
        }

        string scratchDirectory;
        try
        {
            scratchDirectory = AgentInvocationScratchDirectory.EnsureExists(request.RunId, request.AttemptId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Outcome.Failed;
        }

        // Everything below has a scratch directory on disk to clean up — including the reparse
        // rejection immediately below — so this single `finally` is the only cleanup call, and
        // no path through this method can leave that directory (or the reparse point occupying
        // it) behind.
        try
        {
            if (AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(scratchDirectory))
            {
                return Outcome.Failed;
            }

            string schemaPath;
            try
            {
                schemaPath = Path.Combine(scratchDirectory, "schema.json");
                await File.WriteAllTextAsync(schemaPath, JsonSerializer.Serialize(request.OutputSchemaDocument), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Outcome.Failed;
            }

            var resultPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);

            var (executablePath, argumentPrefix) = request.LaunchScriptPath is { } scriptPath
                ? (request.LaunchExecutablePath, (IReadOnlyList<string>)[scriptPath])
                : (request.LaunchExecutablePath, (IReadOnlyList<string>)[]);

            var arguments = new List<string>(argumentPrefix)
            {
                "exec",
                "--json",
                "--output-schema", schemaPath,
                "--output-last-message", resultPath,
                "--sandbox", "read-only",
                "--cd", request.WorkspacePath,
                "--ephemeral",
                "--ignore-user-config",
            };

            if (request.RequestedModel is { } requestedModel)
            {
                arguments.Add("--model");
                arguments.Add(requestedModel);
            }

            if (request.RequestedEffort is { } requestedEffort)
            {
                arguments.Add("--config");
                arguments.Add("model_reasoning_effort=" + requestedEffort);
            }

            arguments.Add("-");

            var executionRequest = new ProcessExecutionRequest
            {
                ExecutablePath = executablePath,
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
                return Outcome.Failed;
            }

            // Preserved for every real result, never collapsed into the Succeeded flag alone: a
            // timeout, a cancellation, and a non-zero exit remain distinguishable host-measured facts.
            var processEvidence = AgentProcessEvidence.FromProcessExecutionResult(result);
            if (result.Outcome != ProcessExecutionOutcome.Exited || result.ExitCode != 0)
            {
                return new Outcome(false, result.StandardOutputTruncated, result.StandardErrorTruncated, null, processEvidence);
            }

            return new Outcome(
                true,
                result.StandardOutputTruncated,
                result.StandardErrorTruncated,
                TryExtractProviderSessionId(result.StandardOutput),
                processEvidence,
                CodexCliTokenUsage.UnlessTruncated(CodexCliTokenUsage.TryRead(result.StandardOutput), result.StandardOutputTruncated));
        }
        finally
        {
            AgentInvocationScratchDirectory.TryDelete(request.RunId, request.AttemptId);
        }
    }

    /// <summary>Never a searched, invented, or PATH-resolved value — must already be a fully
    /// qualified path to an existing file, exactly what a previously-observed launch target's own
    /// components are stored as. Lexical containment and existence alone prove nothing once a
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

    /// <summary>A requested model or effort is untrusted text at this boundary regardless of
    /// where it was previously validated: bounded in length and restricted to the same safe
    /// identifier character set the Codex model-catalog adapter already enforces, so a value can
    /// never carry an injected argument, shell metacharacter, or adversarial byte onto the command
    /// line. <see langword="null"/> (no override requested) is always acceptable.</summary>
    private static bool IsAcceptableAssignmentValue(string? value) =>
        value is null
        || (value.Length is > 0 and <= MaxAssignmentIdentifierLength
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));

    /// <summary>
    /// Only non-secret OS/profile-location values required to locate existing local Codex
    /// authentication — never PATH, a provider token, or an arbitrary environment entry.
    /// <c>CODEX_HOME</c> is forwarded only when this host process already has one set;
    /// <c>USERPROFILE</c> is always forwarded so Codex can locate its default configuration
    /// directory when <c>CODEX_HOME</c> is not set.
    /// </summary>
    private static Dictionary<string, string> BuildEnvironmentAllowlist()
    {
        var environment = new Dictionary<string, string>();

        var userProfile = System.Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrEmpty(userProfile))
        {
            environment["USERPROFILE"] = userProfile;
        }

        var codexHome = System.Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrEmpty(codexHome))
        {
            environment["CODEX_HOME"] = codexHome;
        }

        return environment;
    }

    /// <summary>
    /// Best-effort scan of the already-captured (redacted, capped) stdout, bounded to its first
    /// <see cref="MaxSessionIdScanBytes"/> characters (a character-count prefix, not a
    /// byte-accurate limit), for the documented Codex non-interactive
    /// <c>{"type":"thread.started","thread_id":"..."}</c> event. This is the provider's own
    /// reported correlation reference for the invoked thread — never a resume capability or an
    /// observation of effective access. Only a complete JSON object on its own line with
    /// <c>type == "thread.started"</c> and a nonblank, bounded string <c>thread_id</c> is
    /// accepted; every other event shape — including the legacy arbitrary <c>session_id</c>
    /// property this method previously read, and a line that is malformed or truncated by the
    /// scan prefix itself — is ignored. If the bounded window contains more than one distinct
    /// valid <c>thread_id</c>, this fails closed to <c>null</c> (Unknown) rather than selecting
    /// one, since a single attempt invocation has exactly one provider thread and disagreement
    /// means the value cannot be trusted.
    /// </summary>
    private static string? TryExtractProviderSessionId(string standardOutput)
    {
        var bounded = standardOutput.Length > MaxSessionIdScanBytes ? standardOutput[..MaxSessionIdScanBytes] : standardOutput;

        string? threadId = null;
        foreach (var line in bounded.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            string? candidate;
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!document.RootElement.TryGetProperty("type", out var typeElement)
                    || typeElement.ValueKind != JsonValueKind.String
                    || typeElement.GetString() != "thread.started")
                {
                    continue;
                }

                if (!document.RootElement.TryGetProperty("thread_id", out var threadIdElement)
                    || threadIdElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                candidate = threadIdElement.GetString();
            }
            catch (JsonException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxSessionIdLength)
            {
                continue;
            }

            if (threadId is null)
            {
                threadId = candidate;
            }
            else if (threadId != candidate)
            {
                return null;
            }
        }

        return threadId;
    }
}
