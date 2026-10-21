using Devalente.Shared.Results;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ArmLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.ClaimLocalExecutionSequenceStep;
using DevalCopilot.Application.Features.Runs.Commands.InterruptLocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Commands.ResolveLocalExecutionSequenceStepPreparation;
using DevalCopilot.Application.Features.Runs.Commands.StopLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequence;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionSequenceAvailability;
using DevalCopilot.Application.Tests.Features.Runs.PlanningSequences;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// The reusable real-file-backed-SQLite scene of the ADR-0060 local execution sequence tests. It seeds one Running, claimed ManualAgent
/// run with its Ready owned workspace, active repository lease, latest checkpoint (number 1, fingerprint <see cref="StartFingerprint"/>),
/// observed Codex and Claude runtimes and 1..N enabled verification recipes over a database with production migrations, plus
/// deterministic Git evidence and artifact fakes that count external work.
///
/// <para>
/// Every handler it exposes (<see cref="ArmAsync"/>, <see cref="ObserveAsync"/>, <see cref="ClaimAsync"/>, <see cref="ResolveAsync"/>,
/// <see cref="StopAsync"/>, <see cref="InterruptAsync"/>, <see cref="ReadStatusAsync"/>, the dispatch markers and result recorders) is built
/// over its OWN <see cref="DevalCopilotDbContext"/> from the fixture, so the races between commits are real SQLite races. The claim goes
/// through the real <see cref="ClaimLocalExecutionSequenceStepCommandHandler"/> and a <see cref="LocalExecutionTestMediator"/> that routes
/// to the six real claim handlers. Step results are recorded through the REAL production result recorders (the same commands the
/// supervisors send), after the real dispatch markers, so a whole cycle can be driven deterministically with
/// <see cref="StepAsync"/> and <see cref="DriveAsync"/>. Tests change the world with independent contexts, exactly as a concurrent
/// request would.
/// </para>
///
/// <para>The scene is split by concern across partial files: this one (creation, handles and the lifecycle commands),
/// <c>.Plans</c> (plan and recipe builders), <c>.Results</c> (dispatch markers and result recorders), <c>.Driver</c> (cycle driving) and
/// <c>.Reads</c> (independent reads).</para>
/// </summary>
internal sealed partial class LocalExecutionSequenceTestScene
{
    public static readonly DateTimeOffset Start = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The fingerprint of the starting checkpoint and of the plan lineage attempts.</summary>
    public static readonly string StartFingerprint = new('a', 64);

    /// <summary>The Git HEAD every checkpoint of the scene reports: the fake Git never moves it.</summary>
    public static readonly string Head = new('a', 40);

    private int lineageNext = 1;
    private int resultNumber;

    private LocalExecutionSequenceTestScene(
        SqliteDatabaseFixture fixture, Guid runId, Guid projectId, Guid workspaceId, Guid checkpointId, string workspacePath)
    {
        Fixture = fixture;
        RunId = runId;
        ProjectId = projectId;
        WorkspaceId = workspaceId;
        CheckpointId = checkpointId;
        WorkspacePath = workspacePath;
        Mediator = new LocalExecutionTestMediator(
            () => Fixture.CreateContext(), Evidence, Artifacts, Clock, AttemptProbe, StepProbe);
    }

    public SqliteDatabaseFixture Fixture { get; }

    public Guid RunId { get; }

    public Guid ProjectId { get; }

    public Guid WorkspaceId { get; }

    /// <summary>The starting checkpoint (number 1).</summary>
    public Guid CheckpointId { get; }

    public string WorkspacePath { get; }

    public string FingerprintSha256 => StartFingerprint;

    public AdjustableTimeProvider Clock { get; } = new(Start);

    /// <summary>The fake Git evidence: counts captures and reports <see cref="PlanningSequenceEvidenceReader.Fingerprint"/>. The scene
    /// moves it to each recorded result checkpoint's fingerprint so a later claim sees the source it expects.</summary>
    public PlanningSequenceEvidenceReader Evidence { get; } = new();

    /// <summary>Serves sealed verification output and records every manifest seal and orphan deletion.</summary>
    public DiagnosisArtifactStore Artifacts { get; } = new();

    public IAttemptDurabilityProbe AttemptProbe => new AttemptDurabilityProbe(Fixture.Options);

    public ILocalExecutionStepProbe StepProbe => new LocalExecutionStepProbe(Fixture.Options);

    /// <summary>The mediator the composed claims dispatch through; each composed claim gets its own context.</summary>
    public LocalExecutionTestMediator Mediator { get; }

    /// <summary>The commit counter of fake-Git results recorded by the scene so far.</summary>
    public int ResultNumber => resultNumber;

    /// <summary>
    /// Creates the run, project, workspace, lease, checkpoint, capability snapshots and <paramref name="recipeCount"/> enabled recipes
    /// numbered from one. Pass zero for the "no recipes" refusal, or add the 33rd recipe yourself.
    /// </summary>
    public static async Task<LocalExecutionSequenceTestScene> CreateAsync(
        SqliteDatabaseFixture fixture,
        int recipeCount = 2,
        int maximumAgentAttempts = 16,
        int maximumReviewCorrectionAttempts = 2,
        TimeSpan? maximumAgentInvocationTime = null,
        bool claimRun = true,
        RunExecutionMode mode = RunExecutionMode.ManualAgent)
    {
        await using var context = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Start);
        var run = mode == RunExecutionMode.Legacy
            ? Run.RecordIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Implement the next increment", Start,
                maximumReviewCorrectionAttempts, maximumAgentAttempts, maximumAgentInvocationTime)
            : Run.RecordClassifiedIntent(
                Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), mode, "Implement the next increment", Start,
                maximumReviewCorrectionAttempts, maximumAgentAttempts, maximumAgentInvocationTime);
        if (claimRun)
        {
            run.Claim(Start);
        }

        var workspacePath = $@"C:\workspaces\{Guid.NewGuid():N}";
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, workspacePath, "branch", Head, "main", Start);
        workspace.MarkReady();
        Assert.Equal(1, workspace.ReserveCheckpointNumber());
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Start, Head, StartFingerprint, []);
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
        var scene = new LocalExecutionSequenceTestScene(fixture, run.Id, project.Id, workspace.Id, checkpoint.Id, workspacePath);
        for (var number = 1; number <= recipeCount; number++)
        {
            await scene.AddRecipeAsync(number);
        }

        return scene;
    }

    // ---- availability and arming ------------------------------------------------------------------------------------------------

    public async Task<GetLocalExecutionSequenceAvailabilityQueryResult> ReadAvailabilityAsync(Guid planProposalMessageId)
    {
        await using var context = Fixture.CreateContext();
        var result = await new GetLocalExecutionSequenceAvailabilityQueryHandler(context).HandleAsync(
            new GetLocalExecutionSequenceAvailabilityQuery(RunId, planProposalMessageId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    /// <summary>The arming command of a plan with the confirmation value of an availability read taken now, asserting it is available.</summary>
    public async Task<ArmLocalExecutionSequenceCommand> ArmCommandAsync(Guid planProposalMessageId)
    {
        var availability = await ReadAvailabilityAsync(planProposalMessageId);
        Assert.True(availability.Available, availability.RefusalCode);
        return CommandFrom(availability);
    }

    public ArmLocalExecutionSequenceCommand CommandFrom(GetLocalExecutionSequenceAvailabilityQueryResult availability) => new(
        RunId, availability.PlanProposalMessageId, availability.WorkspaceId!.Value, availability.CheckpointId!.Value,
        availability.FingerprintSha256!, availability.ConfirmationSha256!);

    public async Task<Result<ArmLocalExecutionSequenceCommandResult>> TryArmAsync(
        ArmLocalExecutionSequenceCommand command,
        IGitWorkspaceEvidenceReader? reader = null,
        IDevalCopilotDbContext? db = null,
        IRunEventNotifier? notifier = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        return await new ArmLocalExecutionSequenceCommandHandler(db ?? owned!, reader ?? Evidence, Clock, notifier)
            .HandleAsync(command, CancellationToken.None);
    }

    /// <summary>Reads the availability of the plan and arms it, asserting success.</summary>
    public async Task<ArmLocalExecutionSequenceCommandResult> ArmAsync(Guid planProposalMessageId)
    {
        var result = await TryArmAsync(await ArmCommandAsync(planProposalMessageId));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    // ---- coordinator, claim, resolve, stop, restart ------------------------------------------------------------------------------

    public async Task<Result<ObserveLocalExecutionSequenceCommandResult>> ObserveAsync(
        Guid sequenceId, IDevalCopilotDbContext? db = null, IRunEventNotifier? notifier = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        return await new ObserveLocalExecutionSequenceCommandHandler(db ?? owned!, Clock, notifier).HandleAsync(
            new ObserveLocalExecutionSequenceCommand(sequenceId), CancellationToken.None);
    }

    /// <summary>An observation that must reserve the next step; returns its ticket.</summary>
    public async Task<LocalExecutionStepTicket> ObserveReservingAsync(Guid sequenceId)
    {
        var observed = await ObserveAsync(sequenceId);
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Errors[0].Code : null);
        return Assert.IsType<LocalExecutionStepTicket>(observed.Value.ReservedStep);
    }

    /// <summary>The real claim step handler of the coordinator: composes the existing claim of the ticket's kind through the test
    /// mediator, with a context of its own.</summary>
    public async Task<Result<ClaimLocalExecutionSequenceStepCommandResult>> ClaimAsync(
        LocalExecutionStepTicket ticket, IDevalCopilotDbContext? db = null, LocalExecutionTestMediator? mediator = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        return await new ClaimLocalExecutionSequenceStepCommandHandler(db ?? owned!, mediator ?? Mediator).HandleAsync(
            new ClaimLocalExecutionSequenceStepCommand(ticket), CancellationToken.None);
    }

    public async Task<LocalExecutionStepTicket> ReserveAndClaimAsync(Guid sequenceId)
    {
        var ticket = await ObserveReservingAsync(sequenceId);
        var claim = await ClaimAsync(ticket);
        Assert.True(claim.IsSuccess, claim.IsFailure ? claim.Errors[0].Code : null);
        Assert.Equal(ticket.ReservedOperationId, claim.Value.OperationId);
        return ticket;
    }

    public async Task<Result<ResolveLocalExecutionSequenceStepPreparationCommandResult>> ResolveAsync(
        LocalExecutionStepTicket ticket, string? failureCode, ILocalExecutionStepProbe? probe = null, IDevalCopilotDbContext? db = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        return await new ResolveLocalExecutionSequenceStepPreparationCommandHandler(db ?? owned!, probe ?? StepProbe, Clock).HandleAsync(
            new ResolveLocalExecutionSequenceStepPreparationCommand(ticket, failureCode), CancellationToken.None);
    }

    public async Task<Result<StopLocalExecutionSequenceCommandResult>> StopAsync(Guid sequenceId, IDevalCopilotDbContext? db = null)
    {
        await using var owned = db is null ? Fixture.CreateContext() : null;
        return await new StopLocalExecutionSequenceCommandHandler(db ?? owned!, Clock).HandleAsync(
            new StopLocalExecutionSequenceCommand(RunId, sequenceId), CancellationToken.None);
    }

    /// <summary>The startup closure, exactly as the host runs it before any recovery or supervisor.</summary>
    public async Task<InterruptLocalExecutionSequencesCommandResult> InterruptAsync()
    {
        await using var context = Fixture.CreateContext();
        var result = await new InterruptLocalExecutionSequencesCommandHandler(context, Clock).HandleAsync(
            new InterruptLocalExecutionSequencesCommand(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    public async Task<GetLocalExecutionSequenceQueryResult> ReadStatusAsync()
    {
        await using var context = Fixture.CreateContext();
        var result = await new GetLocalExecutionSequenceQueryHandler(context, Clock).HandleAsync(
            new GetLocalExecutionSequenceQuery(RunId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    /// <summary>The next distinct fake-Git result fingerprint (a lowercase 64-digit hexadecimal value).</summary>
    public string NextFingerprint()
    {
        resultNumber++;
        return new string("bcdef1234567890"[(resultNumber - 1) % 15], 64);
    }
}
