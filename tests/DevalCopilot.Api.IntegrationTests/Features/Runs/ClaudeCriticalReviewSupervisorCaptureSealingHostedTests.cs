using DevalCopilot.Domain.Features.Processes;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0072 (review WEP-R1) at the real <c>ClaudeCriticalReviewSupervisor</c> with the REAL adapter: a captured stream whose writer
/// the host did not prove closed is neither sealed nor deleted, while a proved stream of the same attempt is sealed, and the
/// physical exit, the failed result and the closed diagnostic are recorded unchanged. Only the process and the Git evidence
/// reader are test boundaries.
/// </summary>
public sealed partial class ClaudeCriticalReviewSupervisorHostedTests
{
    [Fact]
    public async Task An_unproved_stdout_writer_is_neither_sealed_nor_deleted_while_the_proved_stderr_is_sealed()
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(_ => SequencedGitWorkspaceEvidenceReader.Matching(Fingerprint));
        var process = new ClaudeEnvelopeProcessDouble(ClaudeEnvelopeProcessDouble.Envelope(ValidAcceptanceFinalResponseJson))
        {
            WriteSinks = true,
            IoCompletion = new ProcessIoCompletion(
                OutputCaptureCompletion.Unknown, OutputCaptureCompletion.Complete, InputDeliveryCompletion.Complete),
        };
        var adapter = new ClaudeCriticalReviewAdapter(process, _artifactStore);
        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        await PreseedRealClaudeLauncherAsync(provider);
        var (runId, attemptId, _, _, _) = await SeedEligibleClaudeCriticalReviewAttemptAsync(provider, evidenceReader);

        var supervisor = CreateSupervisor(provider);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            Assert.Equal(AttemptStatus.Failed, await PollForTerminalStatusAsync(provider, attemptId));
        }
        finally
        {
            using var stopCancellation = new CancellationTokenSource(PollTimeout);
            await supervisor.StopAsync(stopCancellation.Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempt = await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Equal(AgentInvocationDiagnostic.CaptureFault, attempt.AgentInvocationDiagnostic);
        Assert.Equal(0, attempt.AgentProcessExitCode);
        Assert.Equal(OutputCaptureCompletion.Unknown, attempt.AgentStandardOutputCompletion);

        var purposes = await dbContext.Artifacts.AsNoTracking()
            .Where(artifact => artifact.AttemptId == attemptId)
            .Select(artifact => artifact.Purpose)
            .ToListAsync();
        Assert.Contains(ArtifactPurpose.AgentStandardError, purposes);
        Assert.DoesNotContain(ArtifactPurpose.AgentStandardOutput, purposes);
        Assert.True(File.Exists(_artifactStore.GetPartialPath(runId, attemptId, ArtifactPurpose.AgentStandardOutput)));
    }
}
