using System.Text.Json;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// Seeds a Running run with a Ready workspace, an active lease, and observed Codex and Claude capabilities, plus
/// one failed <see cref="AgentOutcome.InvalidStructuredOutput"/> source of each read-only stage with its real
/// ordered inputs — through the Domain factories and transitions, against the host's own database. Every
/// checkpoint carries <see cref="Fingerprint"/> so a fixed-fingerprint evidence reader matches. A seeded sealed
/// response artifact and workspace path let a test prove nothing of the source leaks through the API.
/// </summary>
public static class ReadOnlyFormatRepairApiSeed
{
    public const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string SealedResponsePath = @"runs\repair-secret-run\attempts\a\final.sealed";
    public const string SealedResponseHash = "sha256:repair-secret-hash";
    public const string WorkspacePathPrefix = @"C:\workspaces\repair-secret-workspace";

    public enum Stage
    {
        CriticalReview,
        Resolution,
        CodeReview,
    }

    /// <summary>The seeded run, the failed source, and what a test needs to assert against it.</summary>
    public sealed record Seeded(
        Guid RunId,
        Guid SourceAttemptId,
        int SourceAttemptNumber,
        Guid WorkspaceId,
        Guid CheckpointId,
        IReadOnlyList<Guid> InputMessageIds,
        IReadOnlyList<Guid> VerificationExecutionIds);

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static string RepairSuffix(Stage stage) => stage switch
    {
        Stage.CriticalReview => "critical-review-repair",
        Stage.Resolution => "challenge-resolution-repair",
        _ => "code-review-repair",
    };

    public static AgentResponseContract Contract(Stage stage) => stage switch
    {
        Stage.CriticalReview => AgentResponseContract.CriticalReview,
        Stage.Resolution => AgentResponseContract.ChallengeResolution,
        _ => AgentResponseContract.ImplementationReview,
    };

    public static async Task<Seeded> SeedAsync(DevalCopilotDbContext dbContext, Stage stage)
    {
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Repair project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Repair the next increment", now);
        run.Claim(now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, WorkspacePathPrefix + Guid.NewGuid().ToString("N"), "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), Fingerprint, []);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), now));

        var number = 0;
        var (root, proposal) = AddRoot(dbContext, run.Id, workspace.Id, checkpoint.Id, now, ++number);
        _ = root;

        Attempt source;
        var inputIds = new List<Guid>();
        var executionIds = new List<Guid>();
        var sourceCheckpointId = checkpoint.Id;
        switch (stage)
        {
            case Stage.CriticalReview:
                source = Attempt.ClaimAgentCriticalReview(
                    Guid.NewGuid(), run.Id, ++number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), Timeout, 262144, 524288, now, number);
                inputIds.Add(proposal.Id);
                break;

            case Stage.Resolution:
                var review = Attempt.ClaimAgentCriticalReview(
                    Guid.NewGuid(), run.Id, ++number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), Timeout, 262144, 524288, now, number);
                review.MarkAgentDispatched(now);
                review.CompleteAgent(AgentOutcome.Challenged, Fingerprint, now, CleanExit());
                dbContext.Attempts.Add(review);
                dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, proposal.Id, 0));
                inputIds.Add(proposal.Id);
                for (var index = 0; index < 2; index++)
                {
                    var challenge = CollaborationMessage.Record(
                        Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne,
                        ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
                        ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Challenge, proposal.Id,
                        $"Challenge {index + 1} summary",
                        JsonSerializer.Serialize(new
                        {
                            disputedItem = $"Disputed item {index + 1}",
                            materialImpact = $"Material impact {index + 1}",
                            reasoning = $"Reasoning {index + 1}",
                            alternativeOrQuestion = $"Alternative or question {index + 1}",
                        }),
                        CollaborationMessageProvenance.ProviderObserved, now);
                    dbContext.CollaborationMessages.Add(challenge);
                    inputIds.Add(challenge.Id);
                }

                source = Attempt.ClaimAgentChallengeResolution(
                    Guid.NewGuid(), run.Id, ++number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), Timeout, 262144, 524288, now, number);
                break;

            default:
                // The result checkpoint is the workspace's current one; it carries the same fingerprint the host's
                // fixed evidence reader reports.
                var result = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, now, new string('b', 40), Fingerprint, []);
                dbContext.GitCheckpoints.Add(result);
                sourceCheckpointId = result.Id;
                var acceptanceAttempt = Attempt.ClaimAgentCriticalReview(
                    Guid.NewGuid(), run.Id, ++number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), Timeout, 262144, 524288, now, number);
                acceptanceAttempt.MarkAgentDispatched(now);
                acceptanceAttempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now, CleanExit());
                dbContext.Attempts.Add(acceptanceAttempt);
                dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), acceptanceAttempt.Id, proposal.Id, 0));
                var acceptance = CollaborationMessage.Record(
                    Guid.NewGuid(), run.Id, acceptanceAttempt.Id, CollaborationMessage.ProtocolVersionOne,
                    ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.Acceptance, proposal.Id,
                    "Accepted.", JsonSerializer.Serialize(new { rationale = "Sound." }), CollaborationMessageProvenance.ProviderObserved, now);
                dbContext.CollaborationMessages.Add(acceptance);

                var implementer = Attempt.ClaimAgentImplementation(
                    Guid.NewGuid(), run.Id, ++number, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), Timeout, 262144, 524288, now, number);
                implementer.MarkAgentDispatched(now);
                implementer.CompleteImplementation(AgentOutcome.Implemented, result.Id, now, CleanExit());
                dbContext.Attempts.Add(implementer);
                dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementer.Id, proposal.Id, 0));
                dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementer.Id, acceptance.Id, 1));
                var report = CollaborationMessage.Record(
                    Guid.NewGuid(), run.Id, implementer.Id, CollaborationMessage.ProtocolVersionOne,
                    ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode),
                    ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, proposal.Id,
                    "Added the ledger table and its query.",
                    JsonSerializer.Serialize(new { completedWork = "Added table and query.", verification = "dotnet test" }),
                    CollaborationMessageProvenance.ProviderObserved, now);
                dbContext.CollaborationMessages.Add(report);
                inputIds.Add(report.Id);

                await dbContext.SaveChangesAsync();
                var commands = new List<VerificationCommand>();
                for (var index = 0; index < 2; index++)
                {
                    commands.Add(VerificationCommand.Configure(
                        Guid.NewGuid(), project.Id, index + 1, $"Verification {index + 1}", @"C:\dotnet.exe", ["test"], 300, true, now));
                }

                dbContext.VerificationCommands.AddRange(commands);
                await dbContext.SaveChangesAsync();
                var executions = new List<VerificationExecution>();
                for (var index = 0; index < commands.Count; index++)
                {
                    var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, index + 1, workspace, result, commands[index], now);
                    execution.MarkDispatched(now);
                    execution.Complete(VerificationExecutionOutcome.Exited, 0, result.FingerprintSha256, now);
                    dbContext.VerificationExecutions.Add(execution);
                    executions.Add(execution);
                }

                await dbContext.SaveChangesAsync();
                source = Attempt.ClaimAgentCodeReview(
                    Guid.NewGuid(), run.Id, ++number, workspace.Id, result.Id, Fingerprint, Guid.NewGuid(), Timeout, 262144, 524288, now, number);
                for (var index = 0; index < executions.Count; index++)
                {
                    dbContext.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.Record(
                        Guid.NewGuid(), source.Id, executions[index].VerificationCommandId, executions[index].Id, index));
                    executionIds.Add(executions[index].Id);
                }

                break;
        }

        source.MarkAgentDispatched(now);
        source.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, now, CleanExit());
        dbContext.Attempts.Add(source);
        for (var index = 0; index < inputIds.Count; index++)
        {
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), source.Id, inputIds[index], index));
        }

        dbContext.Artifacts.Add(Artifact.Record(
            Guid.NewGuid(), run.Id, source.Id, ArtifactPurpose.AgentFinalResponse, "application/json",
            SealedResponsePath, SealedResponseHash, 128, false,
            ArtifactCaptureOutcome.Captured, ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, now));
        await dbContext.SaveChangesAsync();
        await ObserveProvidersAsync(dbContext, now);
        return new Seeded(run.Id, source.Id, source.AttemptNumber, workspace.Id, sourceCheckpointId, inputIds, executionIds);
    }

    /// <summary>Marks both host-wide provider capabilities observed (once per database).</summary>
    public static async Task ObserveProvidersAsync(DevalCopilotDbContext dbContext, DateTimeOffset now)
    {
        foreach (var (capability, path) in new[] { (Capability.CodexCli, @"C:\safe\codex.exe"), (Capability.ClaudeCli, @"C:\safe\claude.exe") })
        {
            var snapshot = await dbContext.HostCapabilitySnapshots.SingleAsync(s => s.Capability == capability);
            if (snapshot.LaunchKind is null)
            {
                snapshot.MarkDispatched(now);
                snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, path, null, "1.2.3", now, now.AddMinutes(5));
                await dbContext.SaveChangesAsync();
            }
        }
    }

    private static (Attempt Planner, CollaborationMessage Proposal) AddRoot(
        DevalCopilotDbContext dbContext, Guid runId, Guid workspaceId, Guid checkpointId, DateTimeOffset now, int number)
    {
        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, number, workspaceId, checkpointId, Fingerprint, Guid.NewGuid(), Timeout, 262144, 524288, now, number);
        planner.MarkAgentDispatched(now);
        planner.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now, CleanExit());
        dbContext.Attempts.Add(planner);
        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), runId, planner.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode), CollaborationMessageType.Proposal, null,
            "Add the ledger table and its query.",
            JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            CollaborationMessageProvenance.ProviderObserved, now);
        dbContext.CollaborationMessages.Add(proposal);
        return (planner, proposal);
    }

    private static AgentProcessExecutionEvidence CleanExit() =>
        AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 0, TimeSpan.FromSeconds(1));
}
