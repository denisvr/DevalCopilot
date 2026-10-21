using System.Text.Json;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>Seeds the durable facts ADR-0060 arming reads, with planted sentinel values in every member that must never be exposed.</summary>
internal static class LocalExecutionSequenceApiSeed
{
    public static readonly string Fingerprint = new('a', 64);

    public const string SentinelExecutable = @"C:\sentinel-tools\SENTINEL_EXECUTABLE_5d1c.exe";
    public const string SentinelArgument = "--sentinel-argument-9b7e";

    public static async Task<LocalExecutionSequenceApiScene> SeedAsync(
        DevalCopilotDbContext db,
        int recipeCount = 2,
        RunExecutionMode mode = RunExecutionMode.ManualAgent,
        bool running = true,
        Action<Run>? configureRun = null,
        string recipeName = "Unit tests")
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var repositoryPath = $@"C:\repos\{Guid.NewGuid():N}";
        var workspacePath = $@"C:\workspaces\{Guid.NewGuid():N}";
        var project = Project.Register(Guid.NewGuid(), "Local execution sequence", repositoryPath, now);
        var run = mode == RunExecutionMode.Legacy
            ? Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Implement the increment", now)
            : Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), mode, "Implement the increment", now);
        configureRun?.Invoke(run);
        if (running)
        {
            run.Claim(now.AddSeconds(1));
        }

        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, workspacePath, "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
        db.Projects.Add(project);
        db.Runs.Add(run);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), now));

        var recipeIds = new List<Guid>();
        for (var number = 1; number <= recipeCount; number++)
        {
            var command = VerificationCommand.Configure(
                Guid.NewGuid(), project.Id, number, $"{recipeName} {number}", SentinelExecutable, [SentinelArgument, $"--n{number}"], 60, true, now);
            recipeIds.Add(command.Id);
            db.VerificationCommands.Add(command);
        }

        var planner = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, now, 1);
        planner.MarkAgentDispatched(now);
        planner.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        db.Attempts.Add(planner);
        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, planner.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal, null, "Add the ledger table and its query.",
            JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }),
            CollaborationMessageProvenance.ProviderObserved, now);
        db.CollaborationMessages.Add(proposal);

        var review = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, now, 2);
        review.MarkAgentDispatched(now);
        review.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
        db.Attempts.Add(review);
        db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, proposal.Id, sequence: 0));
        db.CollaborationMessages.Add(CollaborationMessage.Record(
            Guid.NewGuid(), run.Id, review.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance, proposal.Id, "Accepted as proposed.",
            JsonSerializer.Serialize(new { rationale = "Sound and complete." }), CollaborationMessageProvenance.ProviderObserved, now));

        await db.SaveChangesAsync();
        return new LocalExecutionSequenceApiScene(
            run.Id, project.Id, workspace.Id, checkpoint.Id, proposal.Id, recipeIds, workspacePath, repositoryPath);
    }
}
