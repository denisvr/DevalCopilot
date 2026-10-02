using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Seeds the evidence the verification-diagnosis endpoint tests need directly against Domain and the run-owned
/// artifact store: an implemented ExecutionReport on the current checkpoint, a failed (and optionally a passed) verification with
/// real sealed output, and a completed diagnosis with its pinned inputs and findings. Raw seeding, not production-written
/// evidence: the hosted journey test writes the same evidence through the production commands.</summary>
internal static class VerificationDiagnosisApiSeed
{
    public const string SentinelExecutable = @"C:\sentinel-tools\build-tool.exe";
    public const string SentinelArgument = "--sentinel-argument-9317";
    public const string SentinelOutput = "SENTINEL-OUTPUT-4410 error CS1002: ; expected";

    public sealed record Run(Guid RunId, Guid WorkspaceId, Guid ResultCheckpointId, Guid ReportId);

    public sealed record Verification(Guid CommandId, Guid ExecutionId);

    public static async Task<Run> SeedImplementedRunAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Diagnosis project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = DevalCopilot.Domain.Features.Runs.Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Diagnose the failing verification", now);
        run.Claim(now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        dbContext.GitWorkspaces.Add(workspace);
        var startingCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), CodeReviewApiWebApplicationFactory.MatchingFingerprint, []);
        var resultCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, 2, now, new string('b', 40), CodeReviewApiWebApplicationFactory.MatchingFingerprint.Replace('a', 'b'), []);
        dbContext.GitCheckpoints.AddRange(startingCheckpoint, resultCheckpoint);
        dbContext.RepositoryMutationLeases.Add(
            RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        await dbContext.SaveChangesAsync();

        var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(snapshot => snapshot.Capability == Capability.CodexCli);
        codex.MarkDispatched(now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
        await dbContext.SaveChangesAsync();

        var reportId = await RequestCodeReviewEndpointTests.SeedImplementedExecutionAsync(factory, run.Id, workspace.Id, resultCheckpoint.Id);
        return new Run(run.Id, workspace.Id, resultCheckpoint.Id, reportId);
    }

    /// <summary>One enabled command whose latest execution on the result checkpoint exited with the given code; a failed
    /// execution gets real sealed stdout/stderr rows through the run-owned artifact store.</summary>
    public static async Task<Verification> SeedVerificationAsync(
        WebApplicationFactory<Program> factory, Run seed, int commandNumber, int exitCode)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var store = scope.ServiceProvider.GetRequiredService<IVerificationOutputArtifactStore>();
        var now = DateTimeOffset.UtcNow;
        var workspace = await dbContext.GitWorkspaces.SingleAsync(candidate => candidate.Id == seed.WorkspaceId);
        var checkpoint = await dbContext.GitCheckpoints.SingleAsync(candidate => candidate.Id == seed.ResultCheckpointId);

        var command = VerificationCommand.Configure(
            Guid.NewGuid(), workspace.ProjectId, commandNumber, $"Check {commandNumber}", SentinelExecutable, [SentinelArgument], 300, true, now);
        dbContext.VerificationCommands.Add(command);
        await dbContext.SaveChangesAsync();

        var next = (await dbContext.VerificationExecutions.Where(candidate => candidate.ProjectId == workspace.ProjectId).Select(candidate => (int?)candidate.ExecutionNumber).MaxAsync() ?? 0) + 1;
        var execution = VerificationExecution.Claim(Guid.NewGuid(), workspace.ProjectId, next, workspace, checkpoint, command, now);
        execution.MarkDispatched(now);
        execution.Complete(VerificationExecutionOutcome.Exited, exitCode, checkpoint.FingerprintSha256, now);
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync();

        if (exitCode != 0)
        {
            foreach (var (purpose, text) in new[]
                     {
                         (VerificationOutputPurpose.StandardOutput, SentinelOutput),
                         (VerificationOutputPurpose.StandardError, "stderr of the failed command"),
                     })
            {
                var partial = store.GetPartialPath(execution.Id, purpose);
                Directory.CreateDirectory(Path.GetDirectoryName(partial)!);
                await File.WriteAllTextAsync(partial, text);
                var sealedFile = await store.SealAsync(execution.Id, purpose, CancellationToken.None)
                    ?? throw new InvalidOperationException("The verification output could not be sealed.");
                dbContext.VerificationOutputArtifacts.Add(VerificationOutputArtifact.Record(
                    Guid.NewGuid(), execution.Id, purpose, sealedFile.RelativeStoragePath, sealedFile.ContentHash, sealedFile.ByteLength,
                    truncated: false, VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, now));
            }

            await dbContext.SaveChangesAsync();
        }

        return new Verification(command.Id, execution.Id);
    }

    /// <summary>The production snapshot digest of an arbitrary persisted (command, execution) pair, as a claim would pin it.</summary>
    internal static async Task<string> SnapshotAsync(DevalCopilotDbContext db, Guid commandId, Guid executionId)
    {
        var command = await db.VerificationCommands.AsNoTracking().SingleAsync(candidate => candidate.Id == commandId);
        var execution = await db.VerificationExecutions.AsNoTracking().SingleAsync(candidate => candidate.Id == executionId);
        var rows = await db.VerificationOutputArtifacts.AsNoTracking().Where(output => output.VerificationExecutionId == executionId).ToListAsync();
        VerificationDiagnosisEvidence.Output? Pick(VerificationOutputPurpose purpose) => rows.Where(row => row.Purpose == purpose)
            .Select(row => new VerificationDiagnosisEvidence.Output(row.Id, row.RelativeStoragePath, row.ByteLength, row.ContentHash, row.Truncated, row.CaptureOutcome))
            .SingleOrDefault();
        var failed = execution.Status == VerificationExecutionStatus.Failed;
        return VerificationDiagnosisSnapshot.Compute(new VerificationDiagnosisEvidence.Entry(
            command, execution, failed, failed ? Pick(VerificationOutputPurpose.StandardOutput) : null, failed ? Pick(VerificationOutputPurpose.StandardError) : null));
    }

    /// <summary>A completed diagnosis of the seeded report that recorded <paramref name="findingCount"/> findings, pinning the
    /// given verification in claimed order.</summary>
    public static async Task<(Guid AttemptId, IReadOnlyList<Guid> FindingIds)> SeedCompletedDiagnosisAsync(
        WebApplicationFactory<Program> factory, Run seed, IReadOnlyList<Verification> verification, int findingCount, int attemptNumber)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var checkpoint = await dbContext.GitCheckpoints.SingleAsync(candidate => candidate.Id == seed.ResultCheckpointId);

        var attempt = Attempt.ClaimAgentVerificationDiagnosis(
            Guid.NewGuid(), seed.RunId, attemptNumber, seed.WorkspaceId, checkpoint.Id, checkpoint.FingerprintSha256, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, now, requestedModel: null, requestedEffort: null, agentBudgetSlot: attemptNumber);
        attempt.MarkAgentDispatched(now.AddSeconds(1));
        attempt.CompleteAgent(
            AgentOutcome.DiagnosisFindingsRecorded, checkpoint.FingerprintSha256, now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, seed.ReportId, 0));
        for (var index = 0; index < verification.Count; index++)
        {
            dbContext.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.RecordDiagnosisSnapshot(
                Guid.NewGuid(), attempt.Id, verification[index].CommandId, verification[index].ExecutionId, index,
                await SnapshotAsync(dbContext, verification[index].CommandId, verification[index].ExecutionId)));
        }

        var findingIds = new List<Guid>();
        for (var index = 0; index < findingCount; index++)
        {
            var finding = CollaborationMessage.RecordAgent(
                attempt,
                Guid.NewGuid(),
                ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
                CollaborationMessageType.ReviewFinding,
                seed.ReportId,
                $"The query is missing a semicolon {index + 1}.",
                JsonSerializer.Serialize(new
                {
                    severity = "high",
                    category = "correctness",
                    evidence = $"The build output reports a missing semicolon {index + 1}.",
                    requiredChange = $"Add the missing semicolon {index + 1}.",
                }),
                now.AddSeconds(2));
            dbContext.CollaborationMessages.Add(finding);
            findingIds.Add(finding.Id);
        }

        await dbContext.SaveChangesAsync();
        return (attempt.Id, findingIds);
    }

    /// <summary>A newer execution of the same command on the same checkpoint: it becomes the command's latest, so any diagnosis
    /// pinned to the earlier execution no longer applies.</summary>
    public static async Task AddLaterExecutionAsync(WebApplicationFactory<Program> factory, Run seed, Guid commandId, int exitCode)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var workspace = await dbContext.GitWorkspaces.SingleAsync(candidate => candidate.Id == seed.WorkspaceId);
        var checkpoint = await dbContext.GitCheckpoints.SingleAsync(candidate => candidate.Id == seed.ResultCheckpointId);
        var command = await dbContext.VerificationCommands.SingleAsync(candidate => candidate.Id == commandId);
        var latest = await dbContext.VerificationExecutions.Where(candidate => candidate.ProjectId == workspace.ProjectId).MaxAsync(candidate => candidate.ExecutionNumber);
        var execution = VerificationExecution.Claim(Guid.NewGuid(), workspace.ProjectId, latest + 1, workspace, checkpoint, command, now);
        execution.MarkDispatched(now);
        execution.Complete(VerificationExecutionOutcome.Exited, exitCode, checkpoint.FingerprintSha256, now);
        dbContext.VerificationExecutions.Add(execution);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Spends the run's whole shared review-correction allowance with finished, unsuccessful correction attempts.</summary>
    public static async Task SpendCorrectionAllowanceAsync(WebApplicationFactory<Program> factory, Run seed)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var checkpoint = await dbContext.GitCheckpoints.SingleAsync(candidate => candidate.Id == seed.ResultCheckpointId);
        var used = await dbContext.Attempts.CountAsync(attempt => attempt.RunId == seed.RunId);
        var allowance = await dbContext.Runs.Where(run => run.Id == seed.RunId).Select(run => run.MaximumReviewCorrectionAttempts).SingleAsync();
        for (var index = 0; index < allowance; index++)
        {
            var number = used + index + 1;
            var attempt = Attempt.ClaimAgentReviewCorrection(
                Guid.NewGuid(), seed.RunId, number, seed.WorkspaceId, checkpoint.Id, checkpoint.FingerprintSha256, Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 262144, 524288, now, number);
            attempt.MarkAgentDispatched(now.AddSeconds(1));
            attempt.CompleteReviewCorrection(AgentOutcome.CorrectionNoChangesProduced, null, now.AddSeconds(2), processEvidence: TestProcessEvidence.CleanExit);
            dbContext.Attempts.Add(attempt);
        }

        await dbContext.SaveChangesAsync();
    }
}
