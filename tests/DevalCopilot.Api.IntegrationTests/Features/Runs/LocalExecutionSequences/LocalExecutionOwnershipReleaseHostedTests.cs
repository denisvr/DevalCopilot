using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Policies.WorkspaceExecutions;
using DevalCopilot.Application.Features.Runs.Commands.ReleaseLocalExecutionOwnership;
using DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionOwnershipRelease;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences.LocalExecutionSequenceHostedAssertions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>
/// ADR-0065 through the REAL hosted supervisors and coordinator: the original host's supervisor receives the opaque ticket from the
/// committed dispatch marker and hands it, with the adapter's physical termination fact, to the ordinary result recorder of BOTH
/// operation families (the Claude implementation attempt and the verification execution). A restarted host, which holds no ticket, can
/// still read the committed observation and perform the Human release; a result whose termination was not observed has no supported
/// release; and the release never rewrites the failed result or the ended cycle.
/// </summary>
public sealed class LocalExecutionOwnershipReleaseHostedTests
{
    private static readonly ProcessTerminationObservation Observed = new(137, TimeSpan.FromMilliseconds(4));

    private static async Task<WorkspaceExecutionStatus> WorkspaceAsync(LocalExecutionSequenceHostedHarness harness, LocalExecutionSequenceLineage lineage) =>
        await harness.InDbAsync(async db => (await WorkspaceExecutionPolicy.AssessAsync(
            db, lineage.ProjectId, lineage.WorkspaceId, null, null, CancellationToken.None)).Status);

    private static async Task<GetLocalExecutionOwnershipReleaseQueryResult> ReadAsync(LocalExecutionSequenceHostedHarness harness, Guid runId, Guid stepId)
    {
        var read = await harness.SendAsync(new GetLocalExecutionOwnershipReleaseQuery(runId, stepId));
        Assert.True(read.IsSuccess, read.IsFailure ? read.Errors[0].Code : null);
        return read.Value;
    }

    public static TheoryData<string> VerificationEnds => ["timeout", "cancelled"];

    [Theory]
    [MemberData(nameof(VerificationEnds))]
    public async Task A_hosted_verification_that_ends_without_exit_proof_but_with_observed_termination_is_released_by_one_human_confirmation(string end)
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.Verification = call => call.RecipeNumber != 1
            ? LocalExecutionSequenceVerificationBehavior.Passes
            : end == "timeout"
                ? LocalExecutionSequenceVerificationBehavior.TimesOutTerminated
                : LocalExecutionSequenceVerificationBehavior.IsCancelledTerminated;
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        var steps = await harness.StepsAsync(sequence.Id);
        Assert.Equal(["Implementation", "Verification@1.0"], Trail(steps));
        var target = steps.Last(step => step.OperationId is not null);

        Assert.Equal(WorkspaceExecutionStatus.VerificationActive, await WorkspaceAsync(harness, lineage));
        var read = await ReadAsync(harness, lineage.RunId, target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        var facts = read.Candidate!.Facts;
        Assert.Equal(LocalExecutionOperationFamily.Verification, facts.Family);
        Assert.Equal(end == "timeout" ? ProcessOutcome.TimedOut : ProcessOutcome.Cancelled, facts.ProcessOutcome);
        Assert.Null(facts.SemanticExitCode);
        Assert.Equal(end == "timeout" ? 137 : 1, facts.PhysicalExitCode);
        var witness = await harness.InDbAsync(db => db.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync(candidate => candidate.StepId == target.Id));
        Assert.Equal(harness.HostLaunch.Id, witness.HostLaunchId);

        var command = new ReleaseLocalExecutionOwnershipCommand(
            lineage.RunId, target.Id, facts.ObservationId, read.Candidate.ConfirmationVersion, read.Candidate.ConfirmationSha256,
            read.Candidate.ConfirmationExpiresAtUtc);
        var released = await harness.SendAsync(command);

        Assert.True(released.IsSuccess, released.IsFailure ? released.Errors[0].Code : null);
        Assert.Equal(WorkspaceExecutionStatus.Idle, await WorkspaceAsync(harness, lineage));
        var execution = Assert.Single(await harness.ExecutionsAsync(lineage.ProjectId));
        Assert.Equal(end == "timeout" ? VerificationExecutionStatus.TimedOut : VerificationExecutionStatus.Cancelled, execution.Status);
        Assert.Null(execution.ExitCode);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, (await harness.WaitForSequenceAsync(sequence.Id, _ => true)).State);
        Assert.True((await harness.SendAsync(command)).IsSuccess);
        Assert.Equal(1, await harness.InDbAsync(db => db.LocalExecutionOwnershipReleases.CountAsync()));
    }

    [Fact]
    public async Task A_hosted_claude_implementation_timeout_with_observed_termination_is_released_and_survives_a_restart_without_a_ticket()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.ClaudeEnds.Enqueue((ProcessExecutionOutcome.TimedOut, Observed));
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        Assert.Equal(["Implementation"], Trail(await harness.StepsAsync(sequence.Id)));
        var target = Assert.Single(await harness.StepsAsync(sequence.Id));
        var original = harness.HostLaunch.Id;
        var attempt = Assert.Single(await harness.AgentAttemptsAsync(lineage.RunId), candidate => candidate.AgentResponseContract == AgentResponseContract.ImplementationReport);
        Assert.Equal(ProcessOutcome.TimedOut, attempt.AgentProcessOutcome);
        Assert.Null(attempt.AgentProcessExitCode);
        Assert.Equal(WorkspaceExecutionStatus.AgentAttemptActive, await WorkspaceAsync(harness, lineage));

        // The restarted host holds no ticket, yet reads the committed observation and performs the Human release.
        await harness.RestartAsync();
        Assert.NotEqual(original, harness.HostLaunch.Id);
        var read = await ReadAsync(harness, lineage.RunId, target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Available, read.Status);
        Assert.Equal(LocalExecutionOperationFamily.Agent, read.Candidate!.Facts.Family);
        Assert.Equal(137, read.Candidate.Facts.PhysicalExitCode);
        var released = await harness.SendAsync(new ReleaseLocalExecutionOwnershipCommand(
            lineage.RunId, target.Id, read.Candidate.Facts.ObservationId, read.Candidate.ConfirmationVersion, read.Candidate.ConfirmationSha256,
            read.Candidate.ConfirmationExpiresAtUtc));

        Assert.True(released.IsSuccess, released.IsFailure ? released.Errors[0].Code : null);
        var after = await ReadAsync(harness, lineage.RunId, target.Id);
        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Released, after.Status);
        Assert.Equal(WorkspaceExecutionStatus.Idle, await WorkspaceAsync(harness, lineage));
        Assert.Equal(original, (await harness.InDbAsync(db => db.LocalExecutionDispatchWitnesses.AsNoTracking().SingleAsync())).HostLaunchId);
        Assert.Equal(1, await harness.InDbAsync(db => db.Events.CountAsync(runEvent => runEvent.EventType == RunEventType.LocalExecutionOwnershipReleased)));
        Assert.Equal(AttemptStatus.Failed, (await harness.AgentAttemptsAsync(lineage.RunId)).Single(candidate => candidate.Id == attempt.Id).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_hosted_result_without_observed_termination_has_no_supported_release_and_stays_held(bool verification)
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        if (verification)
        {
            harness.Process.Verification = call => call.RecipeNumber != 1
                ? LocalExecutionSequenceVerificationBehavior.Passes
                : LocalExecutionSequenceVerificationBehavior.TimesOut;
        }
        else
        {
            harness.Process.ClaudeEnds.Enqueue((ProcessExecutionOutcome.TimedOut, null));
        }

        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        Assert.Equal(LocalExecutionSequenceState.NeedsAttention, sequence.State);
        var target = (await harness.StepsAsync(sequence.Id)).Last(step => step.OperationId is not null);

        var read = await ReadAsync(harness, lineage.RunId, target.Id);

        Assert.Equal(LocalExecutionOwnershipReleaseStatus.Unavailable, read.Status);
        Assert.Equal("evidence_missing", read.ReasonCode);
        Assert.Equal(0, await harness.InDbAsync(db => db.LocalExecutionTerminationObservations.CountAsync()));
        Assert.NotEqual(WorkspaceExecutionStatus.Idle, await WorkspaceAsync(harness, lineage));
        Assert.True((await harness.SendAsync(new ReleaseLocalExecutionOwnershipCommand(
            lineage.RunId, target.Id, Guid.NewGuid(), 1, new string('0', 64), DateTimeOffset.UtcNow.AddMinutes(1)))).IsFailure);
    }

    [Fact]
    public async Task Capacity_counts_the_hosted_quarantined_owner_until_the_release_and_never_afterwards()
    {
        await using var harness = new LocalExecutionSequenceHostedHarness();
        var lineage = await harness.SeedLineageAsync();
        harness.Process.ClaudeEnds.Enqueue((ProcessExecutionOutcome.Cancelled, Observed));
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        var sequence = await harness.WaitForEndAsync(armed.SequenceId);
        var target = Assert.Single(await harness.StepsAsync(sequence.Id));

        Assert.Equal(1, await harness.InDbAsync(async db => (await ExecutionCapacityPolicy.ReadAsync(db, null, CancellationToken.None)).RecordedOwners));
        var read = await ReadAsync(harness, lineage.RunId, target.Id);
        Assert.True((await harness.SendAsync(new ReleaseLocalExecutionOwnershipCommand(
            lineage.RunId, target.Id, read.Candidate!.Facts.ObservationId, read.Candidate.ConfirmationVersion, read.Candidate.ConfirmationSha256,
            read.Candidate.ConfirmationExpiresAtUtc))).IsSuccess);

        Assert.Equal(0, await harness.InDbAsync(async db => (await ExecutionCapacityPolicy.ReadAsync(db, null, CancellationToken.None)).RecordedOwners));
    }
}
