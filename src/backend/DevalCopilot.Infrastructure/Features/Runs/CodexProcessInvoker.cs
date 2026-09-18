using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>
/// The one shared, bounded Codex CLI invocation contract every Codex adapter in this application
/// reuses: <c>exec --json --output-schema &lt;file&gt; --output-last-message &lt;file&gt; --sandbox
/// read-only --cd &lt;workspace&gt; --ephemeral --ignore-user-config -</c>, with the caller's context
/// manifest written to stdin and stdin then closed. Never passes
/// <c>--dangerously-bypass-approvals-and-sandbox</c>, workspace-write/full-access, hooks, worktree
/// creation, <c>add-dir</c>, a model flag, arbitrary config overrides, or any user-supplied
/// argument. Never searches PATH or a private desktop application layout — the launch target
/// arrives already resolved and is only revalidated here. Extracted out of the original
/// <c>CodexPlanningAdapter</c> so a second Codex role never has to copy this contract; only the
/// output-schema document and the artifact-purpose-scoped sink paths vary by caller.
/// </summary>
internal static class CodexProcessInvoker
{
    internal const int MaxContextManifestReadBytes = 32 * 1024;
    private const int MaxSessionIdScanBytes = 4 * 1024;
    private const int MaxSessionIdLength = 256;

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
        object OutputSchemaDocument);

    internal sealed record Outcome(bool Succeeded, bool StandardOutputTruncated, bool StandardErrorTruncated, string? ProviderSessionId)
    {
        internal static readonly Outcome Failed = new(false, false, false, null);
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
                "-",
            };

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

            if (result.Outcome != ProcessExecutionOutcome.Exited || result.ExitCode != 0)
            {
                return new Outcome(false, result.StandardOutputTruncated, result.StandardErrorTruncated, null);
            }

            return new Outcome(
                true,
                result.StandardOutputTruncated,
                result.StandardErrorTruncated,
                TryExtractProviderSessionId(result.StandardOutput));
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
    /// Best-effort, bounded scan of the already-captured (redacted, capped) stdout for a
    /// provider-reported session identifier on one of Codex's JSONL event lines. Never required;
    /// never trusted for anything beyond this closed, cosmetic field.
    /// </summary>
    private static string? TryExtractProviderSessionId(string standardOutput)
    {
        var bounded = standardOutput.Length > MaxSessionIdScanBytes ? standardOutput[..MaxSessionIdScanBytes] : standardOutput;

        foreach (var line in bounded.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(trimmed);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (document.RootElement.TryGetProperty("session_id", out var sessionIdElement)
                    && sessionIdElement.ValueKind == JsonValueKind.String)
                {
                    var value = sessionIdElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && value.Length <= MaxSessionIdLength)
                    {
                        return value;
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
    }
}
