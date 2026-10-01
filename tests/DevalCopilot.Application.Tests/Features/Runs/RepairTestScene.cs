using System.Text.Json;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// A real, file-backed run for the manual format-repair tests: a claimed (Running) run with a Ready workspace,
/// an active lease, one current checkpoint, and both provider capabilities observed, plus builders for one
/// failed <see cref="AgentOutcome.InvalidStructuredOutput"/> source of each of the three read-only stages
/// (CriticalReviewer, Resolver, CodeReviewer) with real ordered inputs. A source is built with the Domain
/// factories and transitions exactly as production records one (dispatched, clean exit, invalid output), so
/// tests that need a corrupted or incomplete source say so explicitly.
/// </summary>
internal sealed class RepairTestScene
{
    public static readonly DateTimeOffset Now = new(2026, 9, 30, 9, 0, 0, TimeSpan.Zero);
    public static readonly string Fingerprint = new('a', 64);
    public static readonly string ResultFingerprint = new('b', 64);
    public static readonly string CorrectedFingerprint = new('c', 64);
    public const string DotnetExecutablePath = @"C:\dotnet.exe";
    public const string NpmExecutablePath = @"C:\npm.cmd";

    private RepairTestScene(
        SqliteDatabaseFixture fixture, DevalCopilotDbContext db, Project project, Run run, GitWorkspace workspace, GitCheckpoint checkpoint)
    {
        Fixture = fixture;
        Db = db;
        Project = project;
        Run = run;
        Workspace = workspace;
        Checkpoint = checkpoint;
        Lineage = new PlanningLineageSeeder(db, run.Id, workspace.Id, checkpoint.Id, Fingerprint, Now);
    }

    public SqliteDatabaseFixture Fixture { get; }

    public DevalCopilotDbContext Db { get; }

    public Project Project { get; }

    public Run Run { get; }

    public GitWorkspace Workspace { get; }

    /// <summary>Checkpoint number 1 — the current checkpoint for the CriticalReviewer and Resolver stages.</summary>
    public GitCheckpoint Checkpoint { get; }

    public PlanningLineageSeeder Lineage { get; }

    /// <summary>Provider-reported usage recorded on the next failed source a builder creates.</summary>
    public AgentTokenUsageEvidence? SourceTokenUsage { get; set; }

    public static async Task<RepairTestScene> CreateAsync(
        SqliteDatabaseFixture fixture, int maximumAgentAttempts = 16, TimeSpan? maximumAgentInvocationTime = null)
    {
        var db = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(
            Guid.NewGuid(), project.Id, 1, "Repair the next increment", Now,
            maximumAgentAttempts: maximumAgentAttempts, maximumAgentInvocationTime: maximumAgentInvocationTime);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);

        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        codex.MarkDispatched(Now);
        codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Now);
        claude.MarkDispatched(Now);
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "1.2.3", Now, Now.AddMinutes(5));

        db.Projects.Add(project);
        db.Runs.Add(run);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), Now));

        // The capability snapshots are host-wide singletons: a second scene in one database reuses them.
        if (!await db.HostCapabilitySnapshots.AnyAsync())
        {
            db.HostCapabilitySnapshots.AddRange(codex, claude);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return new RepairTestScene(fixture, db, project, run, workspace, checkpoint);
    }

    /// <summary>Records the source's failure exactly as production does: dispatched, then concluded as an
    /// invalid structured output with clean-exit process evidence.</summary>
    public static Attempt Invalidate(Attempt attempt, string fingerprint, AgentTokenUsageEvidence? tokenUsage = null)
    {
        attempt.MarkAgentDispatched(Now);
        attempt.CompleteAgent(AgentOutcome.InvalidStructuredOutput, fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit, tokenUsage: tokenUsage);
        return attempt;
    }

    /// <summary>A failed CriticalReviewer source reviewing the root Proposal, or — with
    /// <paramref name="reviseFirst"/> — the first Resolver revision of a resolved Challenge round.</summary>
    public (Attempt Source, CollaborationMessage Proposal) AddCriticalReviewSource(bool reviseFirst = false)
    {
        var (_, proposal) = Lineage.AddRoot();
        if (reviseFirst)
        {
            proposal = Lineage.AddChallengedRound(proposal).Resolution.RevisedProposal;
        }

        return (AddInvalidCriticalReview(proposal), proposal);
    }

    public Attempt AddInvalidCriticalReview(CollaborationMessage proposal)
    {
        var number = Lineage.ReserveAttemptNumber();
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), Run.Id, number, Workspace.Id, Checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        Invalidate(attempt, Fingerprint, SourceTokenUsage);
        Db.Attempts.Add(attempt);
        Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, proposal.Id, sequence: 0));
        return attempt;
    }

    /// <summary>A failed Resolver source resolving a Challenged review of the root Proposal, or — with
    /// <paramref name="secondRound"/> — of the first Resolver revision.</summary>
    public (Attempt Source, PlanningLineageSeeder.Review Review, CollaborationMessage Proposal) AddResolverSource(
        bool secondRound = false, int challengeCount = 2)
    {
        var (_, proposal) = Lineage.AddRoot();
        if (secondRound)
        {
            proposal = Lineage.AddChallengedRound(proposal).Resolution.RevisedProposal;
        }

        var review = Lineage.AddReview(proposal, AgentOutcome.Challenged, challengeCount);
        return (AddInvalidResolver(proposal, review.Outputs), review, proposal);
    }

    public Attempt AddInvalidResolver(CollaborationMessage proposal, IReadOnlyList<CollaborationMessage> challenges)
    {
        var number = Lineage.ReserveAttemptNumber();
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), Run.Id, number, Workspace.Id, Checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        Invalidate(attempt, Fingerprint, SourceTokenUsage);
        Db.Attempts.Add(attempt);
        Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, proposal.Id, sequence: 0));
        for (var index = 0; index < challenges.Count; index++)
        {
            Db.AttemptInputMessages.Add(
                AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, challenges[index].Id, sequence: index + 1));
        }

        return attempt;
    }

    /// <summary>The chain a CodeReviewer source reviews, and the checkpoint that review is bound to.</summary>
    public sealed record ImplementationScene(
        CollaborationMessage ResolvedPlan,
        CollaborationMessage OriginalPlan,
        CollaborationMessage ExecutionReport,
        GitCheckpoint ReviewCheckpoint,
        string ReviewFingerprint,
        int LastAttemptNumber);

    /// <summary>The commands and their Passed executions (bound to the review checkpoint) a review claims.</summary>
    public sealed record VerificationScene(
        IReadOnlyList<VerificationCommand> Commands, IReadOnlyList<VerificationExecution> Executions);

    /// <summary>The supported initial-implementation plan forms: the Accepted Planner root, a first Resolver revision
    /// with its Decisions, and that revision with its exact optional Acceptance.</summary>
    public enum PlanForm
    {
        AcceptedRoot,
        FirstRevision,
        AcceptedFirstRevision,
    }

    /// <summary>A completed initial implementation: the implemented Proposal with its form's exact ordered inputs, an
    /// Implementer attempt whose result checkpoint (number 2) is the workspace's current one, and its ExecutionReport
    /// replying to the first input. <c>ResolvedPlan</c> is the implemented Proposal and <c>OriginalPlan</c> the Planner root.</summary>
    public ImplementationScene AddInitialImplementation(PlanForm form = PlanForm.AcceptedRoot)
    {
        var resultCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), Workspace.Id, 2, Now, new string('b', 40), ResultFingerprint, []);
        Db.GitCheckpoints.Add(resultCheckpoint);

        var (_, root) = Lineage.AddRoot();
        var resolvedPlan = root;
        var extraInputs = new List<CollaborationMessage>();
        if (form == PlanForm.AcceptedRoot)
        {
            extraInputs.Add(Lineage.AddReview(root, AgentOutcome.Accepted).Outputs[0]);
        }
        else
        {
            var round = Lineage.AddChallengedRound(root, 2).Resolution;
            resolvedPlan = round.RevisedProposal;
            extraInputs.AddRange(round.Decisions);
            if (form == PlanForm.AcceptedFirstRevision)
            {
                extraInputs.Add(Lineage.AddReview(resolvedPlan, AgentOutcome.Accepted).Outputs[0]);
            }
        }

        var implementerNumber = Lineage.ReserveAttemptNumber();
        var implementer = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), Run.Id, implementerNumber, Workspace.Id, Checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, implementerNumber);
        implementer.MarkAgentDispatched(Now);
        implementer.CompleteImplementation(AgentOutcome.Implemented, resultCheckpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
        Db.Attempts.Add(implementer);
        Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementer.Id, resolvedPlan.Id, sequence: 0));
        for (var index = 0; index < extraInputs.Count; index++)
        {
            Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), implementer.Id, extraInputs[index].Id, sequence: index + 1));
        }

        var report = CollaborationMessage.Record(
            Guid.NewGuid(), Run.Id, implementer.Id, CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Implementer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), CollaborationMessageType.ExecutionReport, resolvedPlan.Id,
            "Added the ledger table and its query.",
            JsonSerializer.Serialize(new { completedWork = "Added table and query.", verification = "dotnet test" }),
            CollaborationMessageProvenance.ProviderObserved, Now);
        Db.CollaborationMessages.Add(report);
        return new ImplementationScene(resolvedPlan, root, report, resultCheckpoint, ResultFingerprint, implementerNumber);
    }

    /// <summary>A completed initial implementation, a ChangesRequested review of it, and a successfully applied
    /// correction whose corrected checkpoint (number 3) is now the workspace's current one; the returned
    /// report is the correction's ExecutionReport.</summary>
    public ImplementationScene AddCorrectedImplementation(PlanForm form = PlanForm.AcceptedRoot)
    {
        var initial = AddInitialImplementation(form);
        var correctedCheckpoint = GitCheckpoint.Capture(Guid.NewGuid(), Workspace.Id, 3, Now, new string('c', 40), CorrectedFingerprint, []);
        Db.GitCheckpoints.Add(correctedCheckpoint);

        var reviewNumber = Lineage.ReserveAttemptNumber();
        var review = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), Run.Id, reviewNumber, Workspace.Id, initial.ReviewCheckpoint.Id, initial.ReviewFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now, reviewNumber);
        review.MarkAgentDispatched(Now);
        review.CompleteAgent(AgentOutcome.ReviewChangesRequested, initial.ReviewFingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        var finding = CollaborationMessage.RecordAgent(
            review, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.ReviewFinding, initial.ExecutionReport.Id, "The query misses the requested filter.",
            JsonSerializer.Serialize(new { severity = "high", category = "correctness", evidence = "The filter is absent.", requiredChange = "Add the filter." }),
            Now);
        Db.Attempts.Add(review);
        Db.CollaborationMessages.Add(finding);
        Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), review.Id, initial.ExecutionReport.Id, sequence: 0));

        var correctionNumber = Lineage.ReserveAttemptNumber();
        var correction = Attempt.ClaimAgentReviewCorrection(
            Guid.NewGuid(), Run.Id, correctionNumber, Workspace.Id, initial.ReviewCheckpoint.Id, initial.ReviewFingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(20), 262144, 524288, Now, correctionNumber);
        correction.MarkAgentDispatched(Now);
        correction.CompleteReviewCorrection(AgentOutcome.CorrectionApplied, correctedCheckpoint.Id, Now, processEvidence: TestProcessEvidence.CleanExit);
        Db.Attempts.Add(correction);
        Db.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), correction.Id, initial.ExecutionReport.Id, sequence: 0),
            AttemptInputMessage.Record(Guid.NewGuid(), correction.Id, finding.Id, sequence: 1));
        var revisionResponse = CollaborationMessage.RecordAgent(
            correction, Guid.NewGuid(), ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.RevisionResponse, finding.Id, "Applied the requested filter.",
            JsonSerializer.Serialize(new { disposition = "Addressed", evidence = "The filter was added.", resultingSourceChanges = "Added the filter." }),
            Now);
        var correctedReport = CollaborationMessage.RecordAgent(
            correction, Guid.NewGuid(), initial.ResolvedPlan.Actor, CollaborationMessageType.ExecutionReport, initial.OriginalPlan.Id,
            "Applied the review correction.",
            JsonSerializer.Serialize(new { completedWork = "Added the filter.", verification = "dotnet test" }), Now);
        Db.CollaborationMessages.AddRange(revisionResponse, correctedReport);
        return new ImplementationScene(initial.ResolvedPlan, initial.OriginalPlan, correctedReport, correctedCheckpoint, CorrectedFingerprint, correctionNumber);
    }

    /// <summary>Enabled verification commands (numbered from 1) and one Passed execution each, bound to the
    /// review checkpoint. Saves, because the executions need their commands persisted first.</summary>
    public async Task<VerificationScene> AddPassedVerificationAsync(ImplementationScene implementation, int commandCount = 2)
    {
        await Db.SaveChangesAsync(CancellationToken.None);
        var commands = new List<VerificationCommand>();
        for (var index = 0; index < commandCount; index++)
        {
            commands.Add(VerificationCommand.Configure(
                Guid.NewGuid(), Project.Id, index + 1, $"Verification {index + 1}",
                index % 2 == 0 ? DotnetExecutablePath : NpmExecutablePath, ["test"], 300, true, Now));
        }

        Db.VerificationCommands.AddRange(commands);
        await Db.SaveChangesAsync(CancellationToken.None);

        var executions = commands.Select((command, index) => PassedExecution(implementation.ReviewCheckpoint, command, index + 1)).ToList();
        await Db.SaveChangesAsync(CancellationToken.None);
        return new VerificationScene(commands, executions);
    }

    public VerificationExecution PassedExecution(GitCheckpoint checkpoint, VerificationCommand command, int executionNumber)
    {
        var execution = VerificationExecution.Claim(Guid.NewGuid(), Project.Id, executionNumber, Workspace, checkpoint, command, Now);
        execution.MarkDispatched(Now);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, Now);
        Db.VerificationExecutions.Add(execution);
        return execution;
    }

    /// <summary>A failed CodeReviewer source of the implementation with the given verification set as its
    /// recorded ordered input identity. Not saved.</summary>
    public Attempt AddInvalidCodeReview(ImplementationScene implementation, VerificationScene verification)
    {
        var number = Lineage.ReserveAttemptNumber();
        var attempt = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), Run.Id, number, Workspace.Id, implementation.ReviewCheckpoint.Id, implementation.ReviewFingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(10), 262144, 524288, Now, number);
        Invalidate(attempt, implementation.ReviewFingerprint, SourceTokenUsage);
        Db.Attempts.Add(attempt);
        Db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, implementation.ExecutionReport.Id, sequence: 0));
        for (var index = 0; index < verification.Executions.Count; index++)
        {
            Db.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.Record(
                Guid.NewGuid(), attempt.Id, verification.Executions[index].VerificationCommandId, verification.Executions[index].Id, index));
        }

        return attempt;
    }

    /// <summary>A claimed (undispatched) format repair of a saved source: the same role, checkpoint, and
    /// fingerprint, the source link, and a copy of the source's exact recorded inputs, built with the Domain
    /// factories. Saves.</summary>
    public async Task<Attempt> AddRepairAsync(Attempt source)
    {
        var fingerprint = source.AgentCheckpointFingerprintSha256!;
        var checkpointId = source.AgentGitCheckpointId!.Value;
        var number = Lineage.ReserveAttemptNumber();
        var repair = source.AgentResponseContract switch
        {
            AgentResponseContract.CriticalReview => Attempt.ClaimAgentCriticalReviewWithModelRequest(
                Guid.NewGuid(), Run.Id, number, Workspace.Id, checkpointId, fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, null, null, number, source.Id),
            AgentResponseContract.ChallengeResolution => Attempt.ClaimAgentChallengeResolutionWithAssignment(
                Guid.NewGuid(), Run.Id, number, Workspace.Id, checkpointId, fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, null, null, number, source.Id),
            AgentResponseContract.ImplementationReview => Attempt.ClaimAgentCodeReviewWithAssignment(
                Guid.NewGuid(), Run.Id, number, Workspace.Id, checkpointId, fingerprint, Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 262144, 524288, Now, null, null, number, source.Id),
            _ => throw new InvalidOperationException("Not a read-only repairable source."),
        };
        Db.Attempts.Add(repair);
        await using (var read = Fixture.CreateContext())
        {
            foreach (var input in await read.AttemptInputMessages.AsNoTracking().Where(i => i.AttemptId == source.Id).ToListAsync())
            {
                Db.AttemptInputMessages.Add(
                    AttemptInputMessage.Record(Guid.NewGuid(), repair.Id, input.CollaborationMessageId, input.Sequence));
            }

            foreach (var evidence in await read.AttemptVerificationEvidence.AsNoTracking().Where(e => e.AttemptId == source.Id).ToListAsync())
            {
                Db.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.Record(
                    Guid.NewGuid(), repair.Id, evidence.VerificationCommandId, evidence.VerificationExecutionId, evidence.Sequence));
            }
        }

        await SaveAsync();
        return repair;
    }

    public Task SaveAsync() => Db.SaveChangesAsync(CancellationToken.None);

    /// <summary>Rewrites one stored attempt column directly — the only way to persist the corrupt, unreadable, or
    /// incomplete source rows the Domain itself refuses to record.</summary>
    public async Task CorruptAsync(Guid attemptId, string assignments)
    {
        await using var context = Fixture.CreateContext();
#pragma warning disable EF1002 // Fixed, test-owned assignment text; no external input.
        await context.Database.ExecuteSqlRawAsync($"UPDATE attempts SET {assignments} WHERE Id = {{0}}", attemptId);
        Db.ChangeTracker.Clear();
#pragma warning restore EF1002
    }

    /// <summary>The number of attempts of the run, read from scalar columns only so an attempt with an    /// unreadable enum column still counts.</summary>    public async Task<int> AttemptCountAsync()    {        await using var context = Fixture.CreateContext();        return await context.Attempts.AsNoTracking().CountAsync(a => a.RunId == Run.Id);    }    /// <summary>Forgets every entity this scene tracked, so a following handler run on <see cref="Db"/> reads    /// state a test changed with raw SQL instead of the tracked, stale instances.</summary>    public void Detach() => Db.ChangeTracker.Clear();
    /// <summary>The number of attempts of the run, read from scalar columns only so an attempt with an
    /// unreadable enum column still counts.</summary>
    public async Task<int> AttemptCountAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.Attempts.AsNoTracking().CountAsync(a => a.RunId == Run.Id);
    }

    /// <summary>Forgets every entity this scene tracked, so a following handler run on <see cref="Db"/> reads
    /// state a test changed with raw SQL instead of the tracked, stale instances.</summary>
    public void Detach() => Db.ChangeTracker.Clear();

    /// <summary>Attempts of the run, freshly read.</summary>
    public async Task<List<Attempt>> AttemptsAsync()
    {
        await using var context = Fixture.CreateContext();
        return await context.Attempts.AsNoTracking().Where(a => a.RunId == Run.Id).OrderBy(a => a.AttemptNumber).ToListAsync();
    }
}
