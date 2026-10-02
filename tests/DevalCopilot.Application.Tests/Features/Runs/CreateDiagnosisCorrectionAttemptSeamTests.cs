using DevalCopilot.Application.Features.Runs.Commands.CreateDiagnosisCorrectionAttempt;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.DiagnosisTestScene;
using static DevalCopilot.Application.Tests.Features.Runs.RepairTestScene;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The atomic authority of the diagnosis-origin correction claim (ADR-0018). Git and artifact work happen first, outside any
/// transaction; then one short transaction takes the write lock, re-reads lifecycle, workspace, lease, checkpoint, the exact
/// diagnosis with its report chain, findings, and verification snapshot, the run-wide gates, and the shared allowance
/// untracked, and inserts under the same lock. The handler runs on the very context that seeded (a populated tracker) and an
/// independent context commits drift exactly before BEGIN; the claim must refuse, consume nothing, persist no attempt, input,
/// artifact, or stale escalation, and delete the sealed manifest. Drift committed after a completed claim is still refused by
/// the dispatch gate.
/// </summary>
public sealed class CreateDiagnosisCorrectionAttemptSeamTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Code<T>(Devalente.Shared.Results.Result<T> result) => Assert.Single(result.Errors).Code;

    public static TheoryData<string> Drifts => new()
    {
        "newer-pass",
        "newer-failure",
        "lease-released",
        "new-checkpoint",
        "command-disabled",
        "output-hash",
        "output-length",
        "run-completed",
    };

    private static Task CommitDriftAsync(DiagnosisTestScene scene, string drift) => drift switch
    {
        "newer-pass" => scene.AddExecutionAsync(1, Spec.Passed()),
        "newer-failure" => scene.AddExecutionAsync(1, Spec.Failed("newer", "newer")),
        "lease-released" => scene.ReleaseLeaseAsync(),
        "new-checkpoint" => scene.AddCheckpointAsync(),
        "command-disabled" => scene.SetEnabledAsync(0, false),
        "output-hash" => scene.SqlAsync("UPDATE verification_output_artifacts SET ContentHash = 'sha256:tampered' WHERE Purpose = 'StandardOutput'"),
        "output-length" => scene.SqlAsync("UPDATE verification_output_artifacts SET ByteLength = ByteLength + 1 WHERE Purpose = 'StandardError'"),
        "run-completed" => scene.SqlAsync("UPDATE runs SET Lifecycle = 'Completed'"),
        _ => throw new ArgumentOutOfRangeException(nameof(drift)),
    };

    private async Task<(DiagnosisTestScene Scene, Attempt Diagnosis)> SceneAsync(int priorCorrections)
    {
        var scene = await CreateAsync(_fixture, [Spec.Passed(), Spec.Failed()], maximumAgentInvocationTime: TimeSpan.FromHours(48));
        var diagnosis = (await scene.AddDiagnosisAsync(AgentOutcome.DiagnosisFindingsRecorded, 2)).Attempt;
        await scene.AddCorrectionHistoryAsync(priorCorrections);
        return (scene, diagnosis);
    }

    [Theory]
    [MemberData(nameof(Drifts))]
    public async Task Drift_committed_just_before_begin_refuses_the_claim_and_consumes_nothing(string drift)
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 0);
        var store = scene.Store;
        var before = await scene.CountsAsync();
        var faulting = new FaultInjectingDbContext(scene.Db) { BeforeBeginTransaction = _ => CommitDriftAsync(scene, drift) };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: store);

        Assert.True(result.IsFailure, "The claim must be refused at the transaction seam.");
        var sealedAttemptId = Assert.Single(store.SealedAttemptIds);
        Assert.Contains((scene.Run.Id, sealedAttemptId, ArtifactPurpose.AgentContextManifest), store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Attempts.Where(a => a.Id == sealedAttemptId));
        Assert.Empty(verify.AttemptInputMessages.Where(i => i.AttemptId == sealedAttemptId));
        Assert.Empty(verify.Artifacts.Where(a => a.AttemptId == sealedAttemptId));
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
        Assert.Equal(before.Attempts, await verify.Attempts.CountAsync());
        Assert.Equal(before.Messages, await verify.CollaborationMessages.CountAsync());
        Assert.Empty(verify.ReviewCorrectionAuthorizations);
    }

    [Theory]
    [MemberData(nameof(Drifts))]
    public async Task Drift_committed_just_before_begin_records_no_stale_escalation_when_the_allowance_is_spent(string drift)
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 2);
        var before = await scene.CountsAsync();
        var faulting = new FaultInjectingDbContext(scene.Db) { BeforeBeginTransaction = _ => CommitDriftAsync(scene, drift) };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting);

        Assert.True(result.IsFailure, "The escalation must be refused when the diagnosis no longer applies.");
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.DiagnosisCorrectionEscalations);
        Assert.Equal(before.Messages, await verify.CollaborationMessages.CountAsync());
        Assert.Equal(before.Events, await verify.Events.CountAsync());
        Assert.Equal(before.Attempts, await verify.Attempts.CountAsync());
    }

    [Fact]
    public async Task A_competing_correction_claimed_just_before_begin_is_refused_as_an_active_attempt()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 0);
        var store = scene.Store;
        var faulting = new FaultInjectingDbContext(scene.Db)
        {
            BeforeBeginTransaction = async _ =>
            {
                var winner = await scene.ClaimCorrectionAsync(diagnosis.Id);
                Assert.True(winner.IsSuccess, winner.IsFailure ? Code(winner) : null);
            },
        };

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id, faulting, store: store);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Code(result));
        await using var verify = _fixture.CreateContext();
        Assert.Single(await verify.Attempts.Where(a => a.AgentResponseContract == AgentResponseContract.ReviewCorrection).ToListAsync());
    }

    [Fact]
    public async Task Drift_committed_after_a_completed_claim_is_still_refused_by_the_dispatch_gate_before_any_provider()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 0);
        var claim = await scene.ClaimCorrectionAsync(diagnosis.Id);
        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(claim.Value);
        await scene.AddExecutionAsync(1, Spec.Passed());

        await using var db = _fixture.CreateContext();
        var dispatch = await new Application.Features.Runs.Commands.MarkAgentAttemptDispatched.MarkAgentAttemptDispatchedCommandHandler(
            db, new FixedTimeProvider(Now)).HandleAsync(
            new Application.Features.Runs.Commands.MarkAgentAttemptDispatched.MarkAgentAttemptDispatchedCommand(scene.Run.Id, created.AttemptId),
            CancellationToken.None);

        Assert.True(dispatch.IsFailure);
        Assert.Equal(
            Application.Features.Runs.Commands.MarkAgentAttemptDispatched.MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode,
            Code(dispatch));
    }

    [Fact]
    public async Task A_claim_that_commits_inside_its_transaction_is_durable_and_consumes_one_slot()
    {
        var (scene, diagnosis) = await SceneAsync(priorCorrections: 0);

        var result = await scene.ClaimCorrectionAsync(diagnosis.Id);

        var created = Assert.IsType<CreateDiagnosisCorrectionAttemptCommandResult.AttemptCreated>(result.Value);
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(a => a.Id == created.AttemptId);
        Assert.Equal(AgentResponseContract.ReviewCorrection, attempt.AgentResponseContract);
        Assert.Equal(3, await verify.AttemptInputMessages.CountAsync(i => i.AttemptId == attempt.Id));
        Assert.Single(await verify.Artifacts.Where(a => a.AttemptId == attempt.Id).ToListAsync());
    }
}
