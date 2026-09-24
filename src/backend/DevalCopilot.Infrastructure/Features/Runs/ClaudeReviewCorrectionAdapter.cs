using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Ports;

namespace DevalCopilot.Infrastructure.Features.Runs;

/// <summary>Invokes Claude with the same bounded, no-shell, isolated mutation contract as the
/// established implementation adapter, but uses the ReviewCorrection schema.</summary>
public sealed class ClaudeReviewCorrectionAdapter(IProcessExecutionAdapter processExecutionAdapter, IArtifactStore artifactStore)
    : IClaudeReviewCorrectionAdapter
{
    private const int MaxManifestBytes = 32 * 1024;
    private const int MaxSessionIdLength = 256;

    public async Task<ReviewCorrectionInvocationResult> InvokeAsync(
        ReviewCorrectionInvocationRequest request, CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(request.LaunchExecutablePath)
            || !File.Exists(request.LaunchExecutablePath)
            || AgentInvocationScratchDirectory.PathOrAnyAncestorHasReparsePoint(request.LaunchExecutablePath))
        {
            return Failed();
        }

        var manifest = await artifactStore.VerifyAndReadSealedAsync(
            request.ContextManifestRelativeStoragePath,
            request.ContextManifestByteLength,
            request.ContextManifestContentHash,
            0,
            MaxManifestBytes,
            cancellationToken);
        if (manifest.Status != SealedReadStatus.Ok)
        {
            return Failed();
        }

        var arguments = new List<string>
        {
            "--print", "--input-format", "text", "--output-format", "json",
            "--json-schema", JsonSerializer.Serialize(ReviewCorrectionOutputSchema.BuildSchemaDocument()),
            "--safe-mode", "--restricted", "--disable-slash-commands", "--no-chrome",
            "--permission-prompts", "none", "--prompt-suggestions", "false",
            "--tools", "Read,Edit,Write,Glob,Grep", "--strict-mcp-config",
            "--permission-mode", "acceptEdits", "--no-session-persistence",
            "--session-id", Guid.NewGuid().ToString(),
        };

        ProcessExecutionResult result;
        try
        {
            result = await processExecutionAdapter.ExecuteAsync(new ProcessExecutionRequest
            {
                ExecutablePath = request.LaunchExecutablePath,
                Arguments = arguments,
                WorkingDirectory = request.WorkspacePath,
                ApprovedRoot = request.WorkspacePath,
                EnvironmentVariables = BuildEnvironmentAllowlist(),
                Timeout = request.Timeout,
                MaxBytesPerStream = request.MaxBytesPerStream,
                MaxTotalCapturedBytes = request.MaxTotalCapturedBytes,
                StandardOutputSinkPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, Domain.Features.Runs.ArtifactPurpose.AgentStandardOutput),
                StandardErrorSinkPath = artifactStore.GetPartialPath(request.RunId, request.AttemptId, Domain.Features.Runs.ArtifactPurpose.AgentStandardError),
                StandardInput = Encoding.UTF8.GetBytes(manifest.Text),
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Failed();
        }

        // Preserved for every real result, never collapsed into the closed Exited/Failed
        // classification alone — mirrors ClaudeImplementationAdapter.
        var processEvidence = AgentProcessEvidence.FromProcessExecutionResult(result);
        if (result.Outcome != ProcessExecutionOutcome.Exited || result.ExitCode != 0)
        {
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated, processEvidence);
        }

        var parsed = TryParseEnvelope(result.StandardOutput, out var finalResponse, out var sessionId, out var reportedUsage);
        var tokenUsage = ClaudeCliTokenUsage.UnlessTruncated(reportedUsage, result.StandardOutputTruncated);
        if (!parsed || finalResponse is null)
        {
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated, processEvidence, tokenUsage);
        }

        try
        {
            var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, Domain.Features.Runs.ArtifactPurpose.AgentFinalResponse);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, finalResponse, cancellationToken);
        }
        catch (IOException)
        {
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated, processEvidence, tokenUsage);
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(result.StandardOutputTruncated, result.StandardErrorTruncated, processEvidence, tokenUsage);
        }

        return new ReviewCorrectionInvocationResult(
            ImplementationInvocationOutcome.Exited, result.StandardOutputTruncated, result.StandardErrorTruncated, sessionId, processEvidence,
            tokenUsage);
    }

    /// <summary>Returns true only for a successful, well-shaped envelope. <paramref name="tokenUsage"/>
    /// is set whenever the envelope is structurally valid — including an <c>is_error: true</c> or
    /// empty-result envelope, which still returns false — and stays null for a malformed envelope or
    /// a missing or malformed <c>usage</c> object; usage never decides the return value. See
    /// <see cref="ClaudeCliTokenUsage"/>.</summary>
    private static bool TryParseEnvelope(
        string output, out string? finalResponse, out string? sessionId, out AgentTokenUsage? tokenUsage)
    {
        finalResponse = null;
        sessionId = null;
        tokenUsage = null;
        try
        {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("is_error", out var error)
                || (error.ValueKind != JsonValueKind.True && error.ValueKind != JsonValueKind.False)
                || !root.TryGetProperty("result", out var result))
            {
                return false;
            }

            string? observedSessionId = null;
            if (root.TryGetProperty("session_id", out var session) && session.ValueKind == JsonValueKind.String)
            {
                var value = session.GetString();
                if (string.IsNullOrWhiteSpace(value) || value.Length > MaxSessionIdLength)
                {
                    return false;
                }

                observedSessionId = value;
            }
            else if (root.TryGetProperty("session_id", out session) && session.ValueKind != JsonValueKind.Null)
            {
                return false;
            }

            tokenUsage = ClaudeCliTokenUsage.TryRead(root);
            if (error.ValueKind == JsonValueKind.True)
            {
                return false;
            }

            sessionId = observedSessionId;
            finalResponse = result.ValueKind == JsonValueKind.String ? result.GetString() : result.GetRawText();
            return !string.IsNullOrWhiteSpace(finalResponse);
        }
        catch (JsonException)
        {
            return false;
        }
    }

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

    /// <summary><paramref name="processEvidence"/> is null only when no process result exists;
    /// <paramref name="tokenUsage"/> is null whenever no structurally valid envelope reported
    /// well-shaped usage.</summary>
    private static ReviewCorrectionInvocationResult Failed(
        bool stdoutTruncated = false,
        bool stderrTruncated = false,
        AgentProcessEvidence? processEvidence = null,
        AgentTokenUsage? tokenUsage = null) =>
        new(ImplementationInvocationOutcome.Failed, stdoutTruncated, stderrTruncated, null, processEvidence, tokenUsage);
}
