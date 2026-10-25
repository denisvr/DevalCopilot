using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences.ScriptedResultKind;
using Step = DevalCopilot.Domain.Features.Runs.LocalExecutionStepKind;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionOwnershipReleases;

/// <summary>
/// ADR-0065 physical termination observation, recorded by the original host from its actual returned process result atomically with the
/// ordinary terminal result. It is a distinct physical fact: a timed-out or cancelled process keeps its outcome and null semantic exit
/// code and is never a clean exit. Missing optional evidence preserves ordinary recording; evidence that is foreign to or contradicts
/// the original dispatch refuses the whole result transaction and is never downgraded into trustworthy absence.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseObservationTests : IAsyncLifetime
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(5000);
    private readonly SqliteDatabaseFixture fixture = new();

    public Task InitializeAsync() => fixture.InitializeAsync();

    public Task DisposeAsync() => fixture.DisposeAsync();

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid SequenceId, Guid AttemptId, LocalExecutionDispatchTicket Ticket)> DispatchedImplementationAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 2);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequenceId = (await scene.ArmAsync(plan.ProposalId)).SequenceId;
        var claimed = await scene.ReserveAndClaimAsync(sequenceId);
        var dispatched = await scene.DispatchAgentAsync(claimed.ReservedOperationId);
        Assert.True(dispatched.IsSuccess);
        return (scene, sequenceId, claimed.ReservedOperationId, dispatched.Value.Ticket!);
    }

    private async Task<(LocalExecutionSequenceTestScene Scene, Guid ExecutionId, LocalExecutionDispatchTicket Ticket)> DispatchedVerificationAsync()
    {
        var scene = await LocalExecutionSequenceTestScene.CreateAsync(fixture, 1);
        var plan = await scene.AddAcceptedRootPlanAsync();
        var sequence = await scene.ArmAndDriveAsync(plan.ProposalId, new LocalExecutionStepScript().Verify(1, 0, LeaveRunning));
        Assert.Equal(LocalExecutionSequenceState.AwaitingStepResult, sequence.State);
        var step = await scene.LastLinkedStepAsync();
        Assert.Equal(Step.Verification, step.Kind);
        return (scene, step.OperationId!.Value, scene.TicketOf(step.OperationId!.Value)!);
    }

    private static LocalExecutionDispatchTicket Forge(
        LocalExecutionDispatchTicket original, Guid? witnessId = null, Guid? operationId = null, Guid? stepId = null, Guid? hostLaunchId = null, Guid? nonce = null) =>
        new(
            witnessId ?? original.WitnessId,
            original.Family,
            operationId ?? original.OperationId,
            stepId ?? original.StepId,
            original.SequenceId,
            hostLaunchId ?? original.HostLaunchId,
            nonce ?? original.Nonce,
            original.DispatchedAtUtc);

    private static AgentProcessEvidence TimedOut(ProcessTerminationObservation? termination) =>
        new(ProcessExecutionOutcome.TimedOut, null, Duration) { Termination = termination };

    [Fact]
    public async Task A_physical_termination_is_recorded_with_the_ordinary_result_and_never_changes_it()
    {
        var (scene, _, attemptId, ticket) = await DispatchedImplementationAsync();
        scene.Clock.Advance(TimeSpan.FromSeconds(6));

        var recorded = await scene.TryRecordImplementationAsync(
            attemptId, TimedOut(new ProcessTerminationObservation(137, TimeSpan.FromMilliseconds(4900))), ticket);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        var attempt = await scene.ReadAttemptAsync(attemptId);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, attempt.AgentOutcome);
        Assert.Equal(ProcessOutcome.TimedOut, attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentProcessExitCode);
        Assert.Equal(Duration, attempt.AgentProcessDuration);
        Assert.False(attempt.GetAgentProcessExecutionEvidence()!.IsCleanExit);
        Assert.False(LocalExecutionExitProof.Holds(attempt));

        var observation = Assert.Single(await scene.ReadObservationsAsync());
        var witness = Assert.Single(await scene.ReadWitnessesAsync());
        Assert.Equal(witness.Id, observation.WitnessId);
        Assert.Equal(ProcessOutcome.TimedOut, observation.ProcessOutcome);
        Assert.Null(observation.SemanticExitCode);
        Assert.Equal(Duration, observation.ProcessDuration);
        Assert.Equal(137, observation.PhysicalExitCode);
        Assert.Equal(TimeSpan.FromMilliseconds(4900), observation.ObservedElapsed);
        Assert.Equal(scene.Clock.GetUtcNow(), observation.RecordedAtUtc);
        Assert.True(observation.RecordedAtUtc >= witness.DispatchedAtUtc);
    }

    [Fact]
    public async Task Missing_termination_evidence_preserves_ordinary_recording_without_an_observation()
    {
        var (scene, _, attemptId, ticket) = await DispatchedImplementationAsync();

        var recorded = await scene.TryRecordImplementationAsync(attemptId, TimedOut(null), ticket);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        Assert.Equal(AttemptStatus.Failed, (await scene.ReadAttemptAsync(attemptId)).Status);
        Assert.Empty(await scene.ReadObservationsAsync());
    }

    [Fact]
    public async Task Termination_evidence_without_the_original_ticket_records_no_observation_and_preserves_the_result()
    {
        var (scene, _, attemptId, _) = await DispatchedImplementationAsync();

        var recorded = await scene.TryRecordImplementationAsync(
            attemptId, TimedOut(new ProcessTerminationObservation(137, TimeSpan.FromMilliseconds(4900))), ticket: null);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        Assert.Equal(AttemptStatus.Failed, (await scene.ReadAttemptAsync(attemptId)).Status);
        Assert.Empty(await scene.ReadObservationsAsync());
    }

    public static TheoryData<string> Refusals =>
        ["foreign-nonce", "foreign-host-launch", "foreign-operation", "foreign-step", "foreign-witness", "elapsed-after-duration", "exited-physical-code-differs"];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Foreign_or_contradictory_termination_evidence_refuses_the_whole_result_transaction(string name)
    {
        var (scene, _, attemptId, ticket) = await DispatchedImplementationAsync();
        var observed = new ProcessTerminationObservation(137, TimeSpan.FromMilliseconds(4900));
        var (evidence, offered) = name switch
        {
            "foreign-nonce" => (TimedOut(observed), Forge(ticket, nonce: Guid.NewGuid())),
            "foreign-host-launch" => (TimedOut(observed), Forge(ticket, hostLaunchId: Guid.NewGuid())),
            "foreign-operation" => (TimedOut(observed), Forge(ticket, operationId: Guid.NewGuid())),
            "foreign-step" => (TimedOut(observed), Forge(ticket, stepId: Guid.NewGuid())),
            "foreign-witness" => (TimedOut(observed), Forge(ticket, witnessId: Guid.NewGuid())),
            "elapsed-after-duration" => (TimedOut(new ProcessTerminationObservation(137, Duration + TimeSpan.FromMilliseconds(1))), ticket),
            _ => (
                new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, Duration) { Termination = new ProcessTerminationObservation(2, Duration) },
                ticket),
        };

        var recorded = await scene.TryRecordImplementationAsync(attemptId, evidence, offered);

        Assert.True(recorded.IsFailure);
        Assert.Equal("local_execution_ownership_release.observation_refused", recorded.Errors[0].Code);
        var attempt = await scene.ReadAttemptAsync(attemptId);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.NotNull(attempt.AgentDispatchedAtUtc);
        Assert.Null(attempt.AgentProcessOutcome);
        Assert.Empty(await scene.ReadObservationsAsync());
        Assert.Equal(0, await scene.CountEventsAsync(RunEventType.AgentAttemptCompleted));
    }

    [Fact]
    public async Task An_observed_clean_failed_exit_is_recorded_and_the_operation_is_already_free_by_the_ordinary_rule()
    {
        var (scene, _, attemptId, ticket) = await DispatchedImplementationAsync();

        var recorded = await scene.TryRecordImplementationAsync(
            attemptId,
            new AgentProcessEvidence(ProcessExecutionOutcome.Exited, 1, Duration) { Termination = new ProcessTerminationObservation(1, Duration) },
            ticket);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        var observation = Assert.Single(await scene.ReadObservationsAsync());
        Assert.Equal(ProcessOutcome.Exited, observation.ProcessOutcome);
        Assert.Equal(1, observation.SemanticExitCode);
        Assert.Equal(1, observation.PhysicalExitCode);
        var step = await scene.StepOfAsync(attemptId);
        var read = await scene.ReadReleaseAsync(step.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.NotEligible, read.Status);
        Assert.Equal("already_free", read.ReasonCode);
        Assert.Equal(Domain.Features.Projects.WorkspaceStatus.Ready, (await ReadWorkspaceAsync(scene)).Status);
    }

    [Fact]
    public async Task A_verification_timeout_records_its_observation_with_the_ordinary_result()
    {
        var (scene, executionId, ticket) = await DispatchedVerificationAsync();

        var recorded = await scene.TryRecordVerificationAsync(
            executionId, VerificationExecutionOutcome.TimedOut, null, new ProcessTerminationObservation(137, TimeSpan.FromMilliseconds(4900)), Duration, ticket);

        Assert.True(recorded.IsSuccess, recorded.IsFailure ? recorded.Errors[0].Code : null);
        var execution = await scene.ReadExecutionAsync(executionId);
        Assert.Equal(VerificationExecutionStatus.TimedOut, execution.Status);
        Assert.Equal(VerificationExecutionOutcome.TimedOut, execution.Outcome);
        Assert.Null(execution.ExitCode);
        var observation = Assert.Single(await scene.ReadObservationsAsync());
        Assert.Equal(ProcessOutcome.TimedOut, observation.ProcessOutcome);
        Assert.Null(observation.SemanticExitCode);
        Assert.Equal(137, observation.PhysicalExitCode);
    }

    public static TheoryData<string> VerificationRefusals =>
        ["foreign-nonce", "adapter-failure-outcome", "no-process-duration", "exited-physical-code-differs", "wrong-family-ticket"];

    [Theory]
    [MemberData(nameof(VerificationRefusals))]
    public async Task Foreign_or_contradictory_verification_evidence_refuses_the_whole_result_transaction(string name)
    {
        var (scene, executionId, ticket) = await DispatchedVerificationAsync();
        var observed = new ProcessTerminationObservation(137, TimeSpan.FromMilliseconds(4900));

        var recorded = name switch
        {
            "foreign-nonce" => await scene.TryRecordVerificationAsync(
                executionId, VerificationExecutionOutcome.TimedOut, null, observed, Duration, Forge(ticket, nonce: Guid.NewGuid())),
            "adapter-failure-outcome" => await scene.TryRecordVerificationAsync(
                executionId, VerificationExecutionOutcome.Failed, null, observed, Duration, ticket),
            "no-process-duration" => await scene.TryRecordVerificationAsync(
                executionId, VerificationExecutionOutcome.TimedOut, null, observed, null, ticket),
            "exited-physical-code-differs" => await scene.TryRecordVerificationAsync(
                executionId, VerificationExecutionOutcome.Exited, 1, new ProcessTerminationObservation(2, Duration), Duration, ticket),
            _ => await scene.TryRecordVerificationAsync(
                executionId,
                VerificationExecutionOutcome.TimedOut,
                null,
                observed,
                Duration,
                new LocalExecutionDispatchTicket(
                    ticket.WitnessId, LocalExecutionOperationFamily.Agent, ticket.OperationId, ticket.StepId, ticket.SequenceId,
                    ticket.HostLaunchId, ticket.Nonce, ticket.DispatchedAtUtc)),
        };

        Assert.True(recorded.IsFailure);
        Assert.Equal("local_execution_ownership_release.observation_refused", recorded.Errors[0].Code);
        var execution = await scene.ReadExecutionAsync(executionId);
        Assert.Equal(VerificationExecutionStatus.Running, execution.Status);
        Assert.Null(execution.Outcome);
        Assert.Empty(await scene.ReadObservationsAsync());
    }

    [Fact]
    public async Task Process_evidence_validation_ignores_the_physical_fact()
    {
        var plain = new AgentProcessEvidence(ProcessExecutionOutcome.TimedOut, null, Duration);
        var observed = plain with { Termination = new ProcessTerminationObservation(137, Duration) };

        Assert.False(observed.IsCleanExit);
        Assert.Equal(plain.IsCleanExit, observed.IsCleanExit);
        foreach (var outcome in new[] { AgentOutcome.ProviderInvocationFailed, AgentOutcome.Implemented })
        {
            var withoutFact = AgentProcessEvidenceRecording.Validate(plain, outcome, true, out var plainDomain);
            var withFact = AgentProcessEvidenceRecording.Validate(observed, outcome, true, out var observedDomain);
            Assert.Equal(withoutFact?.Code, withFact?.Code);
            Assert.Equal(plainDomain, observedDomain);
        }

        // A timed-out process with a physically observed exit is still never the success an Implemented outcome requires.
        Assert.NotNull(AgentProcessEvidenceRecording.Validate(observed, AgentOutcome.Implemented, true, out _));
        var fromResult = AgentProcessEvidence.FromProcessExecutionResult(new ProcessExecutionResult
        {
            Outcome = ProcessExecutionOutcome.Cancelled,
            ExitCode = null,
            StandardOutput = string.Empty,
            StandardOutputTruncated = false,
            StandardError = string.Empty,
            StandardErrorTruncated = false,
            Duration = Duration,
            Termination = new ProcessTerminationObservation(1, TimeSpan.FromSeconds(1)),
        });
        Assert.Equal(ProcessExecutionOutcome.Cancelled, fromResult.Outcome);
        Assert.Null(fromResult.ExitCode);
        Assert.Equal(1, fromResult.Termination!.PhysicalExitCode);
        Assert.False(fromResult.IsCleanExit);
    }

    private async Task<GitWorkspace> ReadWorkspaceAsync(LocalExecutionSequenceTestScene scene)
    {
        await using var context = fixture.CreateContext();
        return await context.GitWorkspaces.AsNoTracking().SingleAsync(workspace => workspace.Id == scene.WorkspaceId);
    }
}
