using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests;

/// <summary>
/// Feeds one already-claimed attempt's own sealed context manifest through the REAL provider adapter of its role (all
/// seven, both providers) over a recording process double, and returns the exact standard input that adapter handed the
/// provider process. No provider is started: the double only proves what each adapter would have been given. The adapters
/// read the manifest from the sealed artifact, so this is the same path a supervisor takes at dispatch — including after a
/// restart — and a rebuilt or substituted manifest could not equal the sealed bytes.
/// </summary>
internal static class RealAdapterInstructionProbe
{
    /// <summary>A capture whose AGENTS.md is exactly <paramref name="agentsText"/> and whose CLAUDE.md is proven absent.</summary>
    internal static GitWorkspaceInstructionContext OwnContext(string agentsText) => new(
    [
        new GitWorkspaceInstructionFile(
            "AGENTS.md", GitWorkspaceInstructionStatus.Complete, null, Encoding.UTF8.GetByteCount(agentsText),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(agentsText))).ToLowerInvariant(), agentsText),
        new GitWorkspaceInstructionFile("CLAUDE.md", GitWorkspaceInstructionStatus.Absent, null, null, null, null),
    ]);

    /// <summary>What a provider must have been given: the sealed bytes exactly, carrying the project's own AGENTS.md text once
    /// inside the instruction section, with the second file proven absent and no fixed documentation reference.</summary>
    internal static void AssertDeliversOwnConventions(Delivery delivery, string agentsText, params string[] foreignMarkers)
    {
        Assert.Equal(delivery.SealedManifest, delivery.Stdin);
        using var document = JsonDocument.Parse(delivery.Stdin);
        var section = document.RootElement.GetProperty("projectInstructionContext");
        var sources = section.GetProperty("sources").EnumerateArray().ToArray();
        Assert.Equal("Complete", sources[0].GetProperty("status").GetString());
        Assert.Equal(agentsText, sources[0].GetProperty("text").GetString());
        Assert.Equal("Absent", sources[1].GetProperty("status").GetString());
        var escaped = JsonSerializer.Serialize(agentsText)[1..^1];
        Assert.Equal(1, delivery.Stdin.Split(escaped).Length - 1);
        Assert.False(document.RootElement.TryGetProperty("instructionReferences", out _));
        Assert.DoesNotContain("docs/engineering-context.md", delivery.Stdin, StringComparison.Ordinal);
        foreach (var marker in foreignMarkers)
        {
            Assert.DoesNotContain(marker, delivery.Stdin, StringComparison.Ordinal);
        }
    }

    internal sealed record Delivery(AgentResponseContract Contract, AgentProvider Provider, Guid AttemptId, string SealedManifest, string Stdin);

    internal sealed class CapturingProcessDouble : IProcessExecutionAdapter
    {
        public List<ProcessExecutionRequest> Requests { get; } = [];

        /// <summary>What the provider process would have done on disk, such as writing its final response.</summary>
        public Action<ProcessExecutionRequest>? OnExecute { get; set; }

        public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            OnExecute?.Invoke(request);
            return Task.FromResult(new ProcessExecutionResult
            {
                Outcome = ProcessExecutionOutcome.Exited,
                ExitCode = 0,
                StandardOutput = "{}",
                StandardOutputTruncated = false,
                StandardError = string.Empty,
                StandardErrorTruncated = false,
                Duration = TimeSpan.FromMilliseconds(5),
            });
        }
    }

    internal static async Task<Delivery> DeliverAsync(
        Attempt attempt, Artifact manifest, string workspacePath, IArtifactStore artifactStore, string launchExecutable,
        PlanningImplementationAuthorizationFact? authorization = null)
    {
        var sealedWindow = await artifactStore.VerifyAndReadSealedAsync(
            manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash, 0, 64 * 1024, CancellationToken.None);
        Assert.Equal(SealedReadStatus.Ok, sealedWindow.Status);

        var process = new CapturingProcessDouble();
        var timeout = attempt.AgentTimeout ?? TimeSpan.FromMinutes(10);
        var perStream = attempt.AgentMaxBytesPerStream ?? 256 * 1024;
        var total = attempt.AgentMaxTotalCapturedBytes ?? 512 * 1024;
        var (run, id, path, length, hash) = (attempt.RunId, attempt.Id, manifest.RelativeStoragePath, manifest.ByteLength, manifest.ContentHash);
        var contract = attempt.AgentResponseContract!.Value;

        switch (contract)
        {
            case AgentResponseContract.Proposal:
                await new CodexPlanningAdapter(process, artifactStore).InvokeAsync(
                    new CodexPlanningInvocationRequest(run, id, workspacePath, path, length, hash, launchExecutable, null, timeout, perStream, total),
                    CancellationToken.None);
                break;
            case AgentResponseContract.CriticalReview:
                await new ClaudeCriticalReviewAdapter(process, artifactStore).InvokeAsync(
                    new CriticalReviewInvocationRequest(run, id, workspacePath, path, length, hash, launchExecutable, timeout, perStream, total),
                    CancellationToken.None);
                break;
            case AgentResponseContract.ChallengeResolution:
                await new CodexChallengeResolutionAdapter(process, artifactStore).InvokeAsync(
                    new ChallengeResolutionInvocationRequest(run, id, workspacePath, path, length, hash, launchExecutable, null, timeout, perStream, total),
                    CancellationToken.None);
                break;
            case AgentResponseContract.ImplementationReport:
                await new ClaudeImplementationAdapter(process, artifactStore).InvokeAsync(
                    new ImplementationInvocationRequest(
                        run, id, workspacePath, path, length, hash, launchExecutable, timeout, perStream, total,
                        AdapterContractVersion: attempt.AgentAdapterContractVersion, PlanningAuthorization: authorization),
                    CancellationToken.None);
                break;
            case AgentResponseContract.ImplementationReview:
                await new CodexImplementationReviewAdapter(process, artifactStore).InvokeAsync(
                    new ImplementationReviewInvocationRequest(run, id, workspacePath, path, length, hash, launchExecutable, null, timeout, perStream, total),
                    CancellationToken.None);
                break;
            case AgentResponseContract.ReviewCorrection:
                await new ClaudeReviewCorrectionAdapter(process, artifactStore).InvokeAsync(
                    new ReviewCorrectionInvocationRequest(
                        run, id, workspacePath, path, length, hash, launchExecutable, timeout, perStream, total,
                        AdapterContractVersion: attempt.AgentAdapterContractVersion),
                    CancellationToken.None);
                break;
            case AgentResponseContract.VerificationDiagnosis:
                await new CodexVerificationDiagnosisAdapter(process, artifactStore).InvokeAsync(
                    new VerificationDiagnosisInvocationRequest(run, id, workspacePath, path, length, hash, launchExecutable, null, timeout, perStream, total),
                    CancellationToken.None);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(attempt), contract, "No real adapter is known for this contract.");
        }

        Assert.True(process.Requests.Count == 1, $"The {contract} adapter started {process.Requests.Count} provider processes.");
        var request = process.Requests[0];
        return new Delivery(contract, attempt.AgentProvider!.Value, attempt.Id, sealedWindow.Text, Encoding.UTF8.GetString(request.StandardInput!));
    }
}
