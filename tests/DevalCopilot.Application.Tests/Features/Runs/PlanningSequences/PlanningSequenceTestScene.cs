using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.ArmPlanningSequence;
using DevalCopilot.Application.Features.Runs.Commands.ClaimPlanningSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.ObservePlanningSequence;
using DevalCopilot.Application.Features.Runs.Commands.StopPlanningSequence;
using DevalCopilot.Application.Features.Runs.Policies.PlanningSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;

/// <summary>
/// One Created, pristine ManualAgent run with its Ready, owned workspace, latest checkpoint and observed Codex and Claude runtimes over
/// a real file-backed SQLite database with production migrations, plus deterministic Git and artifact fakes that count external work
/// (ADR-0055). Every handler is built with its own independent <see cref="DevalCopilotDbContext"/> so the races between commits are real
/// SQLite races. A step result is recorded through the Domain transitions and message rows a provider result produces, so the
/// coordinator classifies exactly the durable identities it would see in production; the hosted tests prove the same flow through the
/// production recorders.
/// </summary>
internal sealed class PlanningSequenceTestScene
{
    public static readonly DateTimeOffset Start = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private PlanningSequenceTestScene(SqliteDatabaseFixture fixture, Guid runId, Guid projectId, Guid workspaceId, Guid checkpointId)
    {
        Fixture = fixture;
        RunId = runId;
        ProjectId = projectId;
        WorkspaceId = workspaceId;
        CheckpointId = checkpointId;
    }

    public SqliteDatabaseFixture Fixture { get; }

    public Guid RunId { get; }

    public Guid ProjectId { get; }

    public Guid WorkspaceId { get; }

    public Guid CheckpointId { get; }

    public string FingerprintSha256 => Fingerprint;

    public AdjustableTimeProvider Clock { get; } = new(Start);

    public PlanningSequenceEvidenceReader Evidence { get; } = new();

    public RecordingArtifactStore Artifacts { get; } = new();

    public IAttemptDurabilityProbe AttemptProbe => new AttemptDurabilityProbe(Fixture.Options);

    public IPlanningSequenceStepProbe StepProbe => new PlanningSequenceStepProbe(Fixture.Options);

    public static async Task<PlanningSequenceTestScene> CreateAsync(
        SqliteDatabaseFixture fixture,
        int maximumAgentAttempts = 16,
        TimeSpan? maximumAgentInvocationTime = null,
        RunExecutionMode mode = RunExecutionMode.ManualAgent,
        string objective = "Plan the next increment")
    {
        await using var context = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Start);
        var run = mode == RunExecutionMode.Legacy
            ? Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), objective, Start, maximumAgentAttempts: maximumAgentAttempts,
                maximumAgentInvocationTime: maximumAgentInvocationTime)
            : Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), mode, objective, Start,
                maximumAgentAttempts: maximumAgentAttempts, maximumAgentInvocationTime: maximumAgentInvocationTime);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Start);
        workspace.MarkReady();
        Assert.Equal(1, workspace.ReserveCheckpointNumber());
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Start, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(
            Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), Start);
        context.Projects.Add(project);
        context.Runs.Add(run);
        context.GitWorkspaces.Add(workspace);
        context.GitCheckpoints.Add(checkpoint);
        context.RepositoryMutationLeases.Add(lease);
        if (!await context.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.CodexCli))
        {
            var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Start);
            codex.MarkDispatched(Start);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Start, Start.AddMinutes(5));
            context.HostCapabilitySnapshots.Add(codex);
        }

        if (!await context.HostCapabilitySnapshots.AnyAsync(candidate => candidate.Capability == Capability.ClaudeCli))
        {
            var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, Start);
            claude.MarkDispatched(Start);
            claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\claude.exe", null, "2.1.276", Start, Start.AddMinutes(5));
            context.HostCapabilitySnapshots.Add(claude);
        }

        await context.SaveChangesAsync(CancellationToken.None);
        return new PlanningSequenceTestScene(fixture, run.Id, project.Id, workspace.Id, checkpoint.Id);
    }

    public ArmPlanningSequenceCommand ArmCommand() => new(RunId, WorkspaceId, CheckpointId, Fingerprint);

    public async Task<Result<ArmPlanningSequenceCommandResult>> TryArmAsync(
        ArmPlanningSequenceCommand? command = null, IGitWorkspaceEvidenceReader? reader = null, IDevalCopilotDbContext? db = null,
        IRunEventNotifier? notifier = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        var handler = new ArmPlanningSequenceCommandHandler(db ?? owned!, reader ?? Evidence, Clock, notifier);
        return await handler.HandleAsync(command ?? ArmCommand(), CancellationToken.None);
    }

    public async Task<ArmPlanningSequenceCommandResult> ArmAsync()
    {
        var result = await TryArmAsync();
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<Result<ObservePlanningSequenceCommandResult>> ObserveAsync(Guid sequenceId, IDevalCopilotDbContext? db = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        return await new ObservePlanningSequenceCommandHandler(db ?? owned!, Clock).HandleAsync(
            new ObservePlanningSequenceCommand(sequenceId), CancellationToken.None);
    }

    /// <summary>An observation that must reserve the next step; returns its ticket.</summary>
    public async Task<PlanningSequenceStepTicket> ObserveReservingAsync(Guid sequenceId)
    {
        var observed = await ObserveAsync(sequenceId);
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Errors[0].Code : null);
        return Assert.IsType<PlanningSequenceStepTicket>(observed.Value.ReservedStep);
    }

    public async Task<Result<StopPlanningSequenceCommandResult>> StopAsync(Guid sequenceId, IDevalCopilotDbContext? db = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        return await new StopPlanningSequenceCommandHandler(db ?? owned!, Clock).HandleAsync(
            new StopPlanningSequenceCommand(RunId, sequenceId), CancellationToken.None);
    }

    /// <summary>Runs exactly the existing claim command of the ticket's fixed role with the internal step identity, through the real
    /// handler; the same composition the coordinator performs through the mediator.</summary>
    public async Task<Result<ClaimPlanningSequenceStepCommandResult>> ClaimAsync(
        PlanningSequenceStepTicket ticket,
        IDevalCopilotDbContext? db = null,
        IGitWorkspaceEvidenceReader? reader = null,
        IArtifactStore? store = null,
        IAttemptDurabilityProbe? attemptProbe = null,
        IPlanningSequenceStepProbe? stepProbe = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        var context = db ?? owned!;
        var evidence = reader ?? Evidence;
        var artifacts = store ?? Artifacts;
        var durability = attemptProbe ?? AttemptProbe;
        var steps = stepProbe ?? StepProbe;
        switch (PlanningSequencePolicy.RoleOf(ticket.Ordinal))
        {
            case AgentRole.Planner:
                {
                    var result = await new CreateCodexPlanningAttemptCommandHandler(
                        context, evidence, artifacts, Clock, durability, null, steps).HandleAsync(
                        new CreateCodexPlanningAttemptCommand(RunId) { PlanningStep = ticket }, CancellationToken.None);
                    return result.IsFailure
                        ? Result<ClaimPlanningSequenceStepCommandResult>.Failure(result.Errors)
                        : Result<ClaimPlanningSequenceStepCommandResult>.Success(new(result.Value.AttemptId, result.Value.AttemptNumber));
                }

            case AgentRole.CriticalReviewer:
                {
                    var result = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                        context, evidence, artifacts, Clock, durability, steps).HandleAsync(
                        new CreateClaudeCriticalReviewAttemptCommand(RunId, ticket.InputProposalMessageId) { PlanningStep = ticket },
                        CancellationToken.None);
                    return result.IsFailure
                        ? Result<ClaimPlanningSequenceStepCommandResult>.Failure(result.Errors)
                        : Result<ClaimPlanningSequenceStepCommandResult>.Success(new(result.Value.AttemptId, result.Value.AttemptNumber));
                }

            default:
                {
                    var result = await new CreateChallengeResolutionAttemptCommandHandler(
                        context, evidence, artifacts, Clock, durability, null, steps).HandleAsync(
                        new CreateChallengeResolutionAttemptCommand(RunId, ticket.PredecessorAttemptId) { PlanningStep = ticket },
                        CancellationToken.None);
                    return result.IsFailure
                        ? Result<ClaimPlanningSequenceStepCommandResult>.Failure(result.Errors)
                        : Result<ClaimPlanningSequenceStepCommandResult>.Success(new(result.Value.AttemptId, result.Value.AttemptNumber));
                }
        }
    }

    /// <summary>Reserve (an observation) and claim the next step, asserting both succeed.</summary>
    public async Task<(PlanningSequenceStepTicket Ticket, ClaimPlanningSequenceStepCommandResult Claim)> ReserveAndClaimAsync(Guid sequenceId)
    {
        var ticket = await ObserveReservingAsync(sequenceId);
        var claim = await ClaimAsync(ticket);
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        return (ticket, claim.Value);
    }

    public async Task<PlanningSequence> ReadSequenceAsync(Guid sequenceId)
    {
        await using var context = Fixture.CreateContext();
        return await context.PlanningSequences.AsNoTracking().SingleAsync(candidate => candidate.Id == sequenceId);
    }

    public async Task<List<PlanningSequenceStep>> ReadStepsAsync(Guid sequenceId)
    {
        await using var context = Fixture.CreateContext();
        return await context.PlanningSequenceSteps.AsNoTracking().Where(candidate => candidate.SequenceId == sequenceId)
            .OrderBy(candidate => candidate.Ordinal).ToListAsync();
    }

    public async Task<List<Attempt>> ReadAttemptsAsync()
    {
        await using var context = Fixture.CreateContext();
        return (await context.Attempts.AsNoTracking().Where(candidate => candidate.RunId == RunId).ToListAsync())
            .OrderBy(candidate => candidate.AttemptNumber).ToList();
    }

    public async Task<int> CountEventsAsync(string eventType)
    {
        await using var context = Fixture.CreateContext();
        return await context.Events.AsNoTracking().CountAsync(candidate => candidate.RunId == RunId && candidate.EventType == eventType);
    }

    /// <summary>Dispatches and completes one claimed attempt with its messages, like the supervisors and recorders do. For a challenge
    /// <paramref name="challenges"/> is the count; a resolution decides exactly the recorded input challenges; a depth-two resolution
    /// also records the host-constructed escalation exactly like the production recorder.</summary>
    public async Task CompleteStepAsync(Guid attemptId, StepResult result, int challenges = 1, bool dispatch = true)
    {
        await using var context = Fixture.CreateContext();
        var attempt = await context.Attempts.SingleAsync(candidate => candidate.Id == attemptId);
        var now = Clock.GetUtcNow();
        if (dispatch)
        {
            attempt.MarkAgentDispatched(now);
        }

        switch (result)
        {
            case StepResult.Proposed:
                attempt.CompleteAgent(AgentOutcome.Proposed, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
                context.CollaborationMessages.Add(Message(
                    attempt, AgentRole.Planner, AgentProvider.Codex, CollaborationMessageType.Proposal, null, "Add the ledger table and its query.",
                    PlanningLineageSeeder.ProposalJson("Ledger")));
                break;

            case StepResult.Accepted:
                {
                    attempt.CompleteAgent(AgentOutcome.Accepted, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
                    var proposalId = await SoleInputAsync(context, attempt.Id);
                    context.CollaborationMessages.Add(Message(
                        attempt, AgentRole.CriticalReviewer, AgentProvider.ClaudeCode, CollaborationMessageType.Acceptance, proposalId,
                        "Accepted as proposed.", """{"rationale":"Sound and complete."}"""));
                    break;
                }

            case StepResult.Challenged:
                {
                    attempt.CompleteAgent(AgentOutcome.Challenged, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
                    var proposalId = await SoleInputAsync(context, attempt.Id);
                    for (var index = 0; index < challenges; index++)
                    {
                        context.CollaborationMessages.Add(Message(
                            attempt, AgentRole.CriticalReviewer, AgentProvider.ClaudeCode, CollaborationMessageType.Challenge, proposalId,
                            $"Challenge {index + 1} summary",
                            $$"""{"disputedItem":"Item {{index + 1}}","materialImpact":"Impact","reasoning":"Reasoning","alternativeOrQuestion":"Alternative"}"""));
                    }

                    break;
                }

            case StepResult.Resolved:
                {
                    attempt.CompleteAgent(AgentOutcome.Resolved, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
                    var inputs = await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == attempt.Id)
                        .OrderBy(input => input.Sequence).ToListAsync();
                    var reviewedProposalId = inputs[0].CollaborationMessageId;
                    foreach (var challenge in inputs.Skip(1))
                    {
                        context.CollaborationMessages.Add(Message(
                            attempt, AgentRole.Resolver, AgentProvider.Codex, CollaborationMessageType.Decision, challenge.CollaborationMessageId,
                            "Decision summary", """{"resolution":"accepted","rationale":"Why","resultingPlanChanges":"Changes","nextAction":"None"}"""));
                    }

                    var revised = Message(
                        attempt, AgentRole.Resolver, AgentProvider.Codex, CollaborationMessageType.Proposal, reviewedProposalId,
                        "Revised ledger proposal.", PlanningLineageSeeder.ProposalJson($"Revised {attempt.AttemptNumber}", $"Steps {attempt.AttemptNumber}"));
                    context.CollaborationMessages.Add(revised);
                    await context.SaveChangesAsync();
                    await RecordEscalationIfSecondRoundAsync(context, attempt, revised, inputs.Skip(1).Select(input => input.CollaborationMessageId).ToArray(), now);
                    break;
                }

            case StepResult.InvalidOutput:
                attempt.CompleteAgent(AgentOutcome.InvalidStructuredOutput, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
                break;

            default:
                attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, Fingerprint, now, processEvidence: TestProcessEvidence.CleanExit);
                break;
        }

        await context.SaveChangesAsync();
    }

    private async Task RecordEscalationIfSecondRoundAsync(
        DevalCopilotDbContext context, Attempt resolver, CollaborationMessage secondRevision, Guid[] challengeIds, DateTimeOffset now)
    {
        // Depth two: the reviewed Proposal of this resolver is itself a first revision (authored by an earlier Resolver).
        var inputs = await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == resolver.Id).OrderBy(input => input.Sequence).ToListAsync();
        var reviewed = await context.CollaborationMessages.AsNoTracking().SingleAsync(message => message.Id == inputs[0].CollaborationMessageId);
        var reviewedOwner = await context.Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == reviewed.AttemptId);
        if (reviewedOwner.AgentRole != AgentRole.Resolver)
        {
            return;
        }

        var firstInputs = await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == reviewedOwner.Id)
            .OrderBy(input => input.Sequence).ToListAsync();
        var rootId = firstInputs[0].CollaborationMessageId;
        context.CollaborationMessages.Add(PlanningEscalation.Record(RunId, rootId, reviewed.Id, secondRevision.Id, challengeIds, now));
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> SoleInputAsync(DevalCopilotDbContext context, Guid attemptId) =>
        await context.AttemptInputMessages.AsNoTracking().Where(input => input.AttemptId == attemptId && input.Sequence == 0)
            .Select(input => input.CollaborationMessageId).SingleAsync();

    private CollaborationMessage Message(
        Attempt attempt, AgentRole role, AgentProvider provider, CollaborationMessageType type, Guid? inReplyTo, string summary, string content) =>
        CollaborationMessage.Record(
            Guid.NewGuid(), RunId, attempt.Id, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForAgent(role, provider),
            ParticipantIdentity.ForAgentWithUnknownRole(provider == AgentProvider.Codex ? AgentProvider.ClaudeCode : AgentProvider.Codex),
            type, inReplyTo, summary, content, CollaborationMessageProvenance.ProviderObserved, Clock.GetUtcNow());
}
