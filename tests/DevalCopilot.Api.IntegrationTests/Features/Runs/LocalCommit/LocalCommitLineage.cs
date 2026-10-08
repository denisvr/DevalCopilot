using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Projects.RecordCheckpointReview;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodeReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationReviewResult;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

internal sealed record LocalCommitLineageIds(
    Guid ProjectId,
    Guid RunId,
    Guid WorkspaceId,
    Guid LeaseId,
    Guid CheckpointId,
    Guid ReviewAttemptId,
    Guid ExecutionReportId,
    Guid ExecutionId,
    Guid CommandId,
    Guid HumanReviewId);

/// <summary>
/// Seeds one complete, real review lineage through the production command chain against a real Git scene: Codex proposal,
/// accepted Claude critical review, a Claude implementation whose result checkpoint is observed from the real worktree, one
/// Passed verification execution, an approved CodeReviewer attempt with its message and FutureAgent review, and finally the
/// explicit human checkpoint approval through the protected endpoint. Only the provider results are supplied; every row is written
/// by the host's own handlers.
/// </summary>
internal static class LocalCommitLineage
{
    public static readonly string[] ChangedFiles = ["a.txt", "added/new.txt"];

    public static readonly string[] LaterChangedFiles = ["a.txt"];

    /// <param name="continueFrom">When given, a LATER run is seeded in the same project, workspace and lease (after an earlier
    /// delivery), starting from the workspace's current head, with its own implementation, verification and approvals.</param>
    public static async Task<LocalCommitLineageIds> SeedAsync(
        LocalCommitHost host, LocalCommitScene scene, bool humanApproval = true, int recipes = 1,
        LocalCommitLineageIds? continueFrom = null)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var evidence = scope.ServiceProvider.GetRequiredService<IGitWorkspaceEvidenceReader>();
        var now = DateTimeOffset.UtcNow;

        Project project;
        GitWorkspace workspace;
        RepositoryMutationLease lease;
        if (continueFrom is null)
        {
            project = Project.Register(Guid.NewGuid(), "Local commit project", scene.MainPath, now);
            dbContext.Projects.Add(project);
            workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), project.Id, 1, scene.WorkspacePath, scene.BranchName, scene.BaselineCommit, "main", now);
            workspace.MarkReady();
            dbContext.GitWorkspaces.Add(workspace);
            lease = RepositoryMutationLease.Acquire(
                Guid.NewGuid(), project.Id, workspace.Id, scene.Ownership.PhysicalVolumeSerialNumber,
                Convert.FromHexString(scene.Ownership.PhysicalFileIdHex), now);
            dbContext.RepositoryMutationLeases.Add(lease);
            scene.AdoptOwnership(workspace.Id, project.Id, lease.Id);
        }
        else
        {
            project = await dbContext.Projects.SingleAsync(candidate => candidate.Id == continueFrom.ProjectId);
            workspace = await dbContext.GitWorkspaces.SingleAsync(candidate => candidate.Id == continueFrom.WorkspaceId);
            lease = await dbContext.RepositoryMutationLeases.SingleAsync(candidate => candidate.Id == continueFrom.LeaseId);
        }

        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Implement the ledger", now);
        dbContext.Runs.Add(run);

        var baseline = await evidence.CaptureAsync(scene.WorkspacePath, CancellationToken.None);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, baseline.Outcome);
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, baseline.HeadCommitSha!,
            baseline.FingerprintSha256!, []));
        foreach (var capability in new[] { Capability.CodexCli, Capability.ClaudeCli })
        {
            // Program composition seeds one host-scoped row per capability at startup. Reuse it when present so this real-host
            // fixture records deterministic observed evidence without competing with that composition-owned unique row.
            var existing = await dbContext.HostCapabilitySnapshots.SingleOrDefaultAsync(item => item.Capability == capability);
            var snapshot = existing ?? HostCapabilitySnapshot.Seed(capability, now);
            snapshot.MarkDispatched(now);
            snapshot.RecordSuccess(
                CapabilityLaunchKind.DirectExecutable, $@"C:\fake\{capability}.exe", null, "1.0.0", now, now.AddMinutes(30));
            if (existing is null)
            {
                dbContext.HostCapabilitySnapshots.Add(snapshot);
            }
        }

        await dbContext.SaveChangesAsync();

        var planning = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(planning.IsSuccess, string.Join(';', planning.Errors.Select(error => error.Code)));
        await DispatchAsync(mediator, run.Id, planning.Value.AttemptId);
        var proposal = new ValidatedProposal(
            "Add the ledger table and its query.",
            JsonSerializer.Serialize(new
            {
                scope = "Ledger",
                implementationSteps = "Add the table then the query",
                risks = "Unbounded content",
                verificationPlan = "Tests",
                escalationPoints = "None expected",
            }));
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                run.Id, planning.Value.AttemptId, AgentOutcome.Proposed, baseline.FingerprintSha256, [], proposal, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var proposalMessage = await dbContext.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == planning.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);

        var critical = await mediator.SendAsync(
            new CreateClaudeCriticalReviewAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);
        Assert.True(critical.IsSuccess, string.Join(';', critical.Errors.Select(error => error.Code)));
        await DispatchAsync(mediator, run.Id, critical.Value.AttemptId);
        var accepted = ClaudeCriticalReviewResponseParser.TryParse(JsonSerializer.Serialize(new
        {
            decision = ClaudeCriticalReviewOutputSchema.AcceptDecision,
            summary = "The plan is feasible as written.",
            rationale = "It correctly handles the ledger schema.",
        }));
        Assert.NotNull(accepted);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                run.Id, critical.Value.AttemptId, AgentOutcome.Accepted, baseline.FingerprintSha256, [], accepted, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);

        var implementation = await mediator.SendAsync(new CreateImplementationAttemptCommand(run.Id, proposalMessage.Id), CancellationToken.None);
        Assert.True(implementation.IsSuccess, string.Join(';', implementation.Errors.Select(error => error.Code)));
        await DispatchAsync(mediator, run.Id, implementation.Value.AttemptId);

        var changedFiles = continueFrom is null ? ChangedFiles : LaterChangedFiles;
        scene.WriteWorkspace("a.txt", continueFrom is null ? "approved change\n" : "second approved change\n");
        if (continueFrom is null)
        {
            scene.WriteWorkspace("added/new.txt", "brand new\n");
        }

        var result = await evidence.CaptureAsync(scene.WorkspacePath, CancellationToken.None);
        Assert.Equal(GitWorkspaceEvidenceOutcome.Success, result.Outcome);
        var report = ValidatedImplementationReport.Create(
            "Added the ledger table and its query.", changedFiles, "Changed the files.", string.Empty, string.Empty, "dotnet test");
        var recorded = await mediator.SendAsync(
            new RecordImplementationResultCommand(
                run.Id, implementation.Value.AttemptId, true, result.HeadCommitSha, result.FingerprintSha256, result.ChangedPaths, [],
                report, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None);
        Assert.True(recorded.IsSuccess, string.Join(';', recorded.Errors.Select(error => error.Code)));
        Assert.Equal(AgentOutcome.Implemented, recorded.Value.Outcome);

        var checkpoint = await dbContext.GitCheckpoints.AsNoTracking().SingleAsync(
            candidate => candidate.WorkspaceId == workspace.Id && candidate.FingerprintSha256 == result.FingerprintSha256);
        var executionReport = await dbContext.CollaborationMessages.AsNoTracking().SingleAsync(
            message => message.AttemptId == implementation.Value.AttemptId && message.Type == CollaborationMessageType.ExecutionReport);

        Guid firstCommand = Guid.Empty;
        Guid firstExecution = Guid.Empty;
        var existingCommands = continueFrom is null
            ? []
            : await dbContext.VerificationCommands.Where(candidate => candidate.ProjectId == project.Id && candidate.IsEnabled)
                .OrderBy(candidate => candidate.CommandNumber).ToListAsync();
        for (var number = 1; number <= (continueFrom is null ? recipes : existingCommands.Count); number++)
        {
            // The project aggregate owns both monotonic counters, so a later production endpoint never reuses a seeded number.
            var command = continueFrom is null
                ? VerificationCommand.Configure(
                    Guid.NewGuid(), project.Id, project.ReserveVerificationCommandNumber(), $"Backend tests {number}", @"C:\dotnet.exe",
                    ["test"], 300, true, now)
                : existingCommands[number - 1];
            if (continueFrom is null)
            {
                dbContext.VerificationCommands.Add(command);
            }

            await dbContext.SaveChangesAsync();
            var trackedWorkspace = await dbContext.GitWorkspaces.SingleAsync(candidate => candidate.Id == workspace.Id);
            var trackedCheckpoint = await dbContext.GitCheckpoints.SingleAsync(candidate => candidate.Id == checkpoint.Id);
            var execution = VerificationExecution.Claim(
                Guid.NewGuid(), project.Id, project.ReserveVerificationExecutionNumber(), trackedWorkspace, trackedCheckpoint, command, now);
            execution.MarkDispatched(now);
            execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, now);
            dbContext.VerificationExecutions.Add(execution);
            await dbContext.SaveChangesAsync();
            if (number == 1)
            {
                firstCommand = command.Id;
                firstExecution = execution.Id;
            }
        }

        var review = await mediator.SendAsync(new CreateCodeReviewAttemptCommand(run.Id, executionReport.Id), CancellationToken.None);
        Assert.True(review.IsSuccess, string.Join(';', review.Errors.Select(error => error.Code)));
        await DispatchAsync(mediator, run.Id, review.Value.AttemptId);
        Assert.True((await mediator.SendAsync(
            new RecordImplementationReviewResultCommand(
                run.Id,
                review.Value.AttemptId,
                AgentOutcome.ReviewApproved,
                checkpoint.FingerprintSha256,
                [],
                ValidatedImplementationReview.CreateApproved(
                    "The implementation matches the plan.", "Every step was followed and verification passed.", "None material."),
                null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);

        var humanReviewId = Guid.Empty;
        if (humanApproval)
        {
            humanReviewId = await RecordHumanApprovalAsync(host, project.Id, checkpoint.Id, firstExecution, "Approved");
        }

        return new LocalCommitLineageIds(
            project.Id, run.Id, workspace.Id, lease.Id, checkpoint.Id, review.Value.AttemptId, executionReport.Id, firstExecution,
            firstCommand, humanReviewId);
    }

    public static async Task<Guid> RecordHumanApprovalAsync(
        LocalCommitHost host, Guid projectId, Guid checkpointId, Guid? executionId, string decision)
    {
        using var client = AuthenticatedClient(host);
        var response = await client.PostAsJsonAsync(
            $"/api/projects/{projectId}/reviews", new RecordCheckpointReviewRequest(checkpointId, executionId, "Human", decision));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RecordCheckpointReviewResponse>())!.ReviewId;
    }

    public static HttpClient AuthenticatedClient(LocalCommitHost host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", LocalCommitHost.Secret);
        return client;
    }

    private static async Task DispatchAsync(IApplicationMediator mediator, Guid runId, Guid attemptId) =>
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, attemptId), CancellationToken.None)).IsSuccess);
}
