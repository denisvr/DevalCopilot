using System.Text.Json;
using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateImplementationAttempt;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.PlanningAuthorizationTestSupport;
using static DevalCopilot.Application.Tests.Features.Runs.SecondChallengeRoundTestSupport;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The explicit initial implementation claim of a human-authorized escalated final plan (ADR-0016): the exact
/// ordered inputs and sealed form, atomic single consumption, refusal and rollback behavior with orphan cleanup, the
/// unchanged existing gates, and fresh authority reads at the short serialized commit.</summary>
public sealed class CreateImplementationAttemptHumanAuthorizationTests : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<(EscalatedLineage Lineage, Guid AuthorizationId, Guid InstructionId)> SeedAuthorizedAsync(
        int secondChallengeCount = 2, int maximumAgentAttempts = 16, bool claudeObserved = true)
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext, secondChallengeCount, claudeObserved, maximumAgentAttempts);
        var authorization = await AuthorizeAsync(dbContext, lineage);
        Assert.True(authorization.IsSuccess);
        return (lineage, authorization.Value.AuthorizationId, authorization.Value.HumanInstructionMessageId);
    }

    private AttemptDurabilityProbe Probe => new(_fixture.Options);

    private async Task AssertGrantUnconsumedAsync(Guid runId)
    {
        await using var verify = _fixture.CreateContext();
        var grant = await GrantAsync(verify, runId);
        Assert.Null(grant.ConsumedByAttemptId);
        Assert.Null(grant.ConsumedAtUtc);
        Assert.Empty(verify.Attempts.Where(attempt => attempt.RunId == runId && attempt.AgentRole == AgentRole.Implementer));
    }

    [Fact]
    public async Task An_authorized_final_plan_is_claimed_with_the_exact_ordered_inputs_and_consumes_its_grant_atomically()
    {
        var (lineage, authorizationId, instructionId) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        var result = await ClaimAsync(dbContext, lineage, evidence, store);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.Equal(1, evidence.Captures);
        Assert.Equal(1, store.Seals);
        Assert.Empty(store.DeletedSealedFiles);

        await using var verify = _fixture.CreateContext();
        var attemptId = result.Value.AttemptId;
        Assert.Equal(
            new[] { lineage.FinalProposal.Id }.Concat(lineage.Second.Decisions.Select(decision => decision.Id)).Append(instructionId),
            await InputsAsync(verify, attemptId));
        var grant = await GrantAsync(verify, lineage.RunId);
        Assert.Equal(authorizationId, grant.Id);
        Assert.Equal(attemptId, grant.ConsumedByAttemptId);
        Assert.Equal(Now, grant.ConsumedAtUtc);

        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId);
        Assert.Equal(AgentRole.Implementer, attempt.AgentRole);
        Assert.Equal(AgentResponseContract.ImplementationReport, attempt.AgentResponseContract);
        Assert.Equal(lineage.Scene.Checkpoint.Id, attempt.AgentGitCheckpointId);
        Assert.True(attempt.AgentBudgetSlot.HasValue);
        Assert.Single(verify.Artifacts.Where(artifact => artifact.AttemptId == attemptId && artifact.Purpose == ArtifactPurpose.AgentContextManifest));
    }

    [Fact]
    public async Task The_sealed_manifest_carries_the_distinct_authorized_form_and_never_an_acceptance_or_the_earlier_revision()
    {
        var (lineage, authorizationId, instructionId) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var store = new RecordingArtifactStore();

        var result = await ClaimAsync(dbContext, lineage, store: store);

        Assert.True(result.IsSuccess);
        var manifestText = store.ReadManifest(lineage.RunId, result.Value.AttemptId);
        using var document = JsonDocument.Parse(manifestText);
        var root = document.RootElement;
        Assert.Equal(PlanningImplementationAuthorizationManifest.Boundary, root.GetProperty("humanPlanAuthorizationBoundary").GetString());
        var plan = root.GetProperty("resolvedPlan");
        Assert.Equal(lineage.FinalProposal.Id, plan.GetProperty("proposalMessageId").GetGuid());
        var evidence = plan.GetProperty("resolutionEvidence");
        Assert.Equal("humanAuthorizedEscalatedProposal", evidence.GetProperty("form").GetString());
        Assert.Equal(2, evidence.GetProperty("decisions").GetArrayLength());
        Assert.Equal(
            lineage.SecondReview.Outputs.Select(challenge => challenge.Id),
            evidence.GetProperty("decisions").EnumerateArray().Select(decision => decision.GetProperty("challengeMessageId").GetGuid()));
        var authorization = evidence.GetProperty("humanAuthorization");
        Assert.Equal(authorizationId, authorization.GetProperty("authorizationId").GetGuid());
        Assert.Equal(lineage.Escalation.Id, authorization.GetProperty("escalationMessageId").GetGuid());
        Assert.Equal(instructionId, authorization.GetProperty("humanInstructionMessageId").GetGuid());
        Assert.Equal(PlanningImplementationInstruction.FixedInstruction, authorization.GetProperty("instruction").GetString());
        Assert.Equal(Rationale, authorization.GetProperty("rationale").GetString());
        Assert.DoesNotContain("acceptedSecondReview", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain("acceptedOriginalProposal", manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain(lineage.First.RevisedProposal.Id.ToString(), manifestText, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(manifestText) <= 32 * 1024);
        Assert.True(PlanningImplementationAuthorizationManifest.Agrees(manifestText, new PlanningImplementationAuthorizationFact(
            authorizationId, lineage.Escalation.Id, lineage.FinalProposal.Id, instructionId, Rationale)));
        Assert.False(PlanningImplementationAuthorizationManifest.Agrees(manifestText, null));
    }

    [Fact]
    public async Task Direct_guidance_is_preserved_beside_the_authorization_and_never_truncated()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var store = new RecordingArtifactStore();

        var result = await ClaimAsync(dbContext, lineage, store: store, guidance: "Keep the migration additive.");

        Assert.True(result.IsSuccess);
        var manifestText = store.ReadManifest(lineage.RunId, result.Value.AttemptId);
        Assert.Contains("Keep the migration additive.", manifestText, StringComparison.Ordinal);
        Assert.Contains(Rationale, manifestText, StringComparison.Ordinal);
        await using var verify = _fixture.CreateContext();
        var attempt = await verify.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == result.Value.AttemptId);
        Assert.Equal("Keep the migration additive.", attempt.ReadAgentDirectHumanGuidance().Text);
    }

    [Fact]
    public async Task Without_an_authorization_the_final_plan_stays_refused_before_any_external_work()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        var result = await ClaimAsync(dbContext, lineage, evidence, store);

        Assert.Equal(PlanningLineage.ExhaustedCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
    }

    [Fact]
    public async Task Authority_evidence_that_cannot_fit_the_manifest_is_refused_whole_and_never_truncated()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync(secondChallengeCount: 5);
        await using (var seed = _fixture.CreateContext())
        {
            var oversized = "{\"resolution\":\"accepted\",\"rationale\":\"" + new string('r', 8000)
                + "\",\"resultingPlanChanges\":\"Changes\",\"nextAction\":\"None\"}";
            var decisionIds = lineage.Second.Decisions.Select(decision => decision.Id).ToArray();
            await seed.CollaborationMessages.Where(message => decisionIds.Contains(message.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(message => message.StructuredContentJson, oversized));
        }

        await using var dbContext = _fixture.CreateContext();
        var store = new RecordingArtifactStore();

        var result = await ClaimAsync(dbContext, lineage, store: store);

        Assert.Equal("agent_attempts.context_manifest_too_large", Assert.Single(result.Errors).Code);
        Assert.Equal(0, store.Seals);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }

    [Fact]
    public async Task A_spent_grant_is_never_reused_even_after_the_claimed_attempt_failed()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var first = await ClaimAsync(dbContext, lineage);
        Assert.True(first.IsSuccess);
        var claimed = await dbContext.Attempts.SingleAsync(attempt => attempt.Id == first.Value.AttemptId);
        claimed.MarkAgentDispatched(Now);
        claimed.CompleteImplementation(AgentOutcome.InvalidStructuredOutput, null, Now, processEvidence: TestProcessEvidence.CleanExit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await using var retryContext = _fixture.CreateContext();
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();
        var second = await ClaimAsync(retryContext, lineage, evidence, store);

        Assert.Equal(PlanningImplementationAuthorizationErrors.ClaimConsumedCode, Assert.Single(second.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(first.Value.AttemptId, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Single(verify.Attempts.Where(attempt => attempt.AgentRole == AgentRole.Implementer));
    }

    [Fact]
    public async Task A_newer_independent_planner_proposal_makes_the_grant_stale_and_leaves_it_unconsumed()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        SeederFor(dbContext, lineage.Scene, lineage.Seeder.NextAttemptNumber + 10).AddRoot();
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        var result = await ClaimAsync(dbContext, lineage, evidence, store);

        Assert.Equal(PlanningImplementationAuthorizationErrors.ClaimStaleCode, Assert.Single(result.Errors).Code);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }

    [Fact]
    public async Task A_newer_checkpoint_refuses_the_claim_and_leaves_the_grant_unconsumed()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), lineage.Scene.Workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await ClaimAsync(dbContext, lineage);

        Assert.True(result.IsFailure);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }

    public static TheoryData<string> TamperedRecords => new()
    {
        "instruction-content",
        "instruction-summary",
        "instruction-reply",
        "instruction-actor",
        "grant-final-proposal",
        "grant-fingerprint",
        "grant-workspace",
        "escalation-content",
        "second-escalation",
    };

    [Theory]
    [MemberData(nameof(TamperedRecords))]
    public async Task A_tampered_grant_or_record_fails_closed_before_external_work_and_stays_unconsumed(string tamper)
    {
        var (lineage, authorizationId, instructionId) = await SeedAuthorizedAsync();
        await using var seed = _fixture.CreateContext();
        var instruction = seed.CollaborationMessages.Where(message => message.Id == instructionId);
        var grants = seed.PlanningImplementationAuthorizations.Where(grant => grant.Id == authorizationId);
        switch (tamper)
        {
            case "instruction-content":
                await instruction.ExecuteUpdateAsync(set => set.SetProperty(
                    message => message.StructuredContentJson, "{\"instruction\":\"x\",\"rationale\":\"y\"}"));
                break;
            case "instruction-summary":
                await instruction.ExecuteUpdateAsync(set => set.SetProperty(message => message.Summary, "Human authorized something else."));
                break;
            case "instruction-reply":
                await instruction.ExecuteUpdateAsync(set => set.SetProperty(message => message.InReplyToMessageId, lineage.FinalProposal.Id));
                break;
            case "instruction-actor":
                await instruction.ExecuteUpdateAsync(set => set.SetProperty(message => message.ActorKind, ParticipantKind.Orchestrator));
                break;
            case "grant-final-proposal":
                await grants.ExecuteUpdateAsync(set => set.SetProperty(grant => grant.FinalProposalMessageId, lineage.First.RevisedProposal.Id));
                break;
            case "grant-fingerprint":
                await grants.ExecuteUpdateAsync(set => set.SetProperty(grant => grant.FingerprintSha256, new string('d', 64)));
                break;
            case "grant-workspace":
                await grants.ExecuteUpdateAsync(set => set.SetProperty(grant => grant.WorkspaceId, Guid.NewGuid()));
                break;
            case "escalation-content":
                await seed.CollaborationMessages.Where(message => message.Id == lineage.Escalation.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(message => message.Summary, "Edited escalation."));
                break;
            default:
                seed.CollaborationMessages.Add(PlanningEscalation.Record(
                    lineage.RunId, lineage.Root.Id, lineage.First.RevisedProposal.Id, lineage.FinalProposal.Id,
                    lineage.SecondReview.Outputs.Select(challenge => challenge.Id).ToArray(), Now));
                await seed.SaveChangesAsync(CancellationToken.None);
                break;
        }

        await using var dbContext = _fixture.CreateContext();
        var evidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();
        var result = await ClaimAsync(dbContext, lineage, evidence, store);

        Assert.True(result.IsFailure, tamper);
        Assert.Equal(0, evidence.Captures);
        Assert.Equal(0, store.Seals);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Empty(verify.Attempts.Where(attempt => attempt.AgentRole == AgentRole.Implementer));
    }

    public static TheoryData<string> HardGates => new()
    {
        "budget",
        "lifecycle",
        "mode",
        "active-attempt",
        "provider-unobserved",
        "workspace-not-ready",
        "lease-inactive",
    };

    [Theory]
    [MemberData(nameof(HardGates))]
    public async Task Every_existing_hard_gate_still_refuses_and_retains_the_unconsumed_grant(string gate)
    {
        var (lineage, _, _) = await SeedAuthorizedAsync(maximumAgentAttempts: gate == "budget" ? 5 : 16);
        await using var seed = _fixture.CreateContext();
        switch (gate)
        {
            case "budget":
                // The five Agent attempts of the lineage use the whole run maximum of five.
                break;
            case "lifecycle":
                await seed.Runs.Where(run => run.Id == lineage.RunId).ExecuteUpdateAsync(set => set.SetProperty(run => run.Lifecycle, RunLifecycle.Failed));
                break;
            case "mode":
                await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, lineage.RunId, (int)RunExecutionMode.Simulated);
                break;
            case "active-attempt":
                seed.Attempts.Add(Attempt.ClaimAgent(
                    Guid.NewGuid(), lineage.RunId, 50, lineage.Scene.Workspace.Id, lineage.Scene.Checkpoint.Id, Fingerprint, Guid.NewGuid(),
                    TimeSpan.FromMinutes(10), 262144, 524288, Now, 50));
                await seed.SaveChangesAsync(CancellationToken.None);
                break;
            case "provider-unobserved":
                await seed.HostCapabilitySnapshots.ExecuteDeleteAsync();
                break;
            case "workspace-not-ready":
                await seed.GitWorkspaces.Where(workspace => workspace.Id == lineage.Scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(workspace => workspace.Status, WorkspaceStatus.NeedsAttention));
                break;
            default:
                await seed.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == lineage.Scene.Workspace.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
                break;
        }

        await using var dbContext = _fixture.CreateContext();
        var store = new RecordingArtifactStore();
        var result = await ClaimAsync(dbContext, lineage, store: store);

        Assert.True(result.IsFailure, gate);
        Assert.Equal(0, store.Seals);
        await using var verify = _fixture.CreateContext();
        Assert.Null((await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Empty(verify.Attempts.Where(attempt => attempt.AgentRole == AgentRole.Implementer));
    }

    [Fact]
    public async Task A_competing_claim_that_spends_the_grant_while_the_manifest_is_sealed_is_refused_with_cleanup()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        Guid? winner = null;
        var store = new RecordingArtifactStore(onSeal: async () =>
        {
            await using var other = _fixture.CreateContext();
            var competing = await ClaimAsync(other, lineage);
            winner = competing.IsSuccess ? competing.Value.AttemptId : null;
        });

        var result = await ClaimAsync(dbContext, lineage, store: store);

        Assert.NotNull(winner);
        Assert.True(result.IsFailure);
        Assert.Single(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(winner, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Single(verify.Attempts.Where(attempt => attempt.AgentRole == AgentRole.Implementer));
    }

    [Fact]
    public async Task Simultaneous_claims_spend_the_grant_exactly_once()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var context = _fixture.CreateContext();
            return await ClaimAsync(context, lineage);
        }));

        Assert.Single(results, result => result.IsSuccess);
        await using var verify = _fixture.CreateContext();
        var implementers = await verify.Attempts.AsNoTracking().Where(attempt => attempt.AgentRole == AgentRole.Implementer).ToListAsync();
        Assert.Single(implementers);
        Assert.Equal(implementers[0].Id, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
    }

    // ---- The short serialized commit reads authority afresh ----------------------------------------------------

    private Task<Guid> CompetingClaimAsync(EscalatedLineage lineage) => Task.Run(async () =>
    {
        await using var other = _fixture.CreateContext();
        return (await ClaimAsync(other, lineage)).Value.AttemptId;
    });

    [Fact]
    public async Task A_competing_claim_committed_before_begin_is_seen_and_refuses_with_the_grant_spent_once()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        Guid winner = Guid.Empty;
        var faulting = new FaultInjectingDbContext(dbContext) { BeforeBeginTransaction = async _ => winner = await CompetingClaimAsync(lineage) };
        var store = new RecordingArtifactStore();

        var result = await ClaimHandler(faulting, store: store, probe: Probe).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.NotEqual(Guid.Empty, winner);
        Assert.Single(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(winner, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Single(verify.Attempts.Where(attempt => attempt.AgentRole == AgentRole.Implementer));
    }

    [Theory]
    [InlineData("planner-root")]
    [InlineData("checkpoint")]
    [InlineData("lifecycle")]
    [InlineData("mode")]
    [InlineData("lease")]
    [InlineData("tampered-instruction")]
    public async Task Authority_changed_by_another_connection_before_begin_is_seen_and_consumes_nothing(string change)
    {
        var (lineage, _, instructionId) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            BeforeBeginTransaction = async _ =>
            {
                await using var other = _fixture.CreateContext();
                switch (change)
                {
                    case "planner-root":
                        SeederFor(other, lineage.Scene, lineage.Seeder.NextAttemptNumber + 10).AddRoot();
                        await other.SaveChangesAsync(CancellationToken.None);
                        break;
                    case "checkpoint":
                        other.GitCheckpoints.Add(GitCheckpoint.Capture(
                            Guid.NewGuid(), lineage.Scene.Workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []));
                        await other.SaveChangesAsync(CancellationToken.None);
                        break;
                    case "lifecycle":
                        await other.Runs.Where(run => run.Id == lineage.RunId)
                            .ExecuteUpdateAsync(set => set.SetProperty(run => run.Lifecycle, RunLifecycle.Failed));
                        break;
                    case "mode":
                        await RunExecutionModeTestSupport.SetStoredModeAsync(_fixture, lineage.RunId, (int)RunExecutionMode.Simulated);
                        break;
                    case "lease":
                        await other.RepositoryMutationLeases.Where(lease => lease.WorkspaceId == lineage.Scene.Workspace.Id)
                            .ExecuteUpdateAsync(set => set.SetProperty(lease => lease.Status, LeaseStatus.Released));
                        break;
                    default:
                        await other.CollaborationMessages.Where(message => message.Id == instructionId)
                            .ExecuteUpdateAsync(set => set.SetProperty(message => message.Summary, "Tampered."));
                        break;
                }
            },
        };
        var store = new RecordingArtifactStore();

        var result = await ClaimHandler(faulting, store: store, probe: Probe).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);

        Assert.True(result.IsFailure, change);
        Assert.Single(store.DeletedSealedFiles);
        await AssertGrantUnconsumedAsync(lineage.RunId);
        await using var verify = _fixture.CreateContext();
        Assert.Empty(verify.Artifacts.Where(artifact => artifact.RunId == lineage.RunId && artifact.Purpose == ArtifactPurpose.AgentContextManifest
            && verify.Attempts.Any(attempt => attempt.Id == artifact.AttemptId && attempt.AgentRole == AgentRole.Implementer)));
    }

    [Fact]
    public async Task A_failed_save_rolls_back_the_attempt_and_the_consumption_and_removes_the_orphan_manifest()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(dbContext)
        {
            SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.UpdateExceptionBeforeSave,
        };
        var store = new RecordingArtifactStore();

        var result = await ClaimHandler(faulting, store: store, probe: Probe).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);

        Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }

    [Fact]
    public async Task A_commit_failure_after_the_save_never_loses_a_claim_that_actually_landed()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(dbContext) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.AfterCommit };
        var store = new RecordingArtifactStore();

        var result = await ClaimHandler(faulting, store: store, probe: Probe).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        Assert.Equal(result.Value.AttemptId, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
    }

    [Fact]
    public async Task A_commit_failure_that_did_not_land_consumes_nothing_and_removes_the_manifest()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(dbContext) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };
        var store = new RecordingArtifactStore();

        var result = await ClaimHandler(faulting, store: store, probe: Probe).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);

        Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }

    // ---- Cancellation and failure after the manifest is sealed -------------------------------------------------
    // The sealed manifest is an orphan only when the claim did not commit: the outcome is read from an independent
    // connection after the transaction is rolled back and released, never from the exception or tracked entities.

    private async Task<(Exception? Thrown, Result<CreateImplementationAttemptCommandResult>? Result, RecordingArtifactStore Store)> ClaimWithFaultAsync(
        EscalatedLineage lineage, Action<FaultInjectingDbContext> fault)
    {
        await using var dbContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(dbContext);
        fault(faulting);
        var store = new RecordingArtifactStore();
        try
        {
            var result = await ClaimHandler(faulting, store: store, probe: Probe).HandleAsync(
                new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);
            return (null, result, store);
        }
        catch (Exception exception)
        {
            return (exception, null, store);
        }
    }

    public static TheoryData<string> UncommittedCancellations => new()
    {
        "begin",
        "late-read",
        "before-save",
        "after-save",
        "before-commit",
    };

    [Theory]
    [MemberData(nameof(UncommittedCancellations))]
    public async Task Cancellation_before_the_claim_commits_removes_the_orphan_manifest_and_consumes_nothing(string point)
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();

        var (thrown, result, store) = await ClaimWithFaultAsync(lineage, faulting =>
        {
            switch (point)
            {
                case "begin":
                    faulting.ThrowCancellationOnBeginTransaction = true;
                    break;
                case "late-read":
                    faulting.BeforeSaveChanges = _ => throw new OperationCanceledException("Cancelled at the last read before the save.");
                    break;
                case "before-save":
                    faulting.SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.CancellationBeforeSave;
                    break;
                case "after-save":
                    faulting.SaveChangesFailure = FaultInjectingDbContext.SaveChangesFailureMode.CancellationAfterSave;
                    break;
                default:
                    faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationBeforeCommit;
                    break;
            }
        });

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.Null(result);
        Assert.Single(store.DeletedSealedFiles);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }

    [Fact]
    public async Task Cancellation_after_the_claim_committed_keeps_its_manifest_and_exactly_one_spent_grant()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();

        var (thrown, result, store) = await ClaimWithFaultAsync(
            lineage, faulting => faulting.CommitFailure = FaultInjectingDbContext.CommitFailureMode.CancellationAfterCommit);

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.Null(result);
        Assert.Empty(store.DeletedSealedFiles);
        await using var verify = _fixture.CreateContext();
        var attempt = Assert.Single(verify.Attempts.Where(candidate => candidate.AgentRole == AgentRole.Implementer));
        Assert.Equal(attempt.Id, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        Assert.Single(verify.Artifacts.Where(artifact => artifact.AttemptId == attempt.Id && artifact.Purpose == ArtifactPurpose.AgentContextManifest));
    }

    [Theory]
    [InlineData(FaultInjectingDbContext.CommitFailureMode.BeforeCommitRollbackAlsoThrows, false)]
    [InlineData(FaultInjectingDbContext.CommitFailureMode.AfterCommitRollbackAlsoThrows, true)]
    public async Task A_commit_failure_with_a_failing_rollback_is_still_settled_from_durable_state(
        FaultInjectingDbContext.CommitFailureMode mode, bool landed)
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();

        var (thrown, result, store) = await ClaimWithFaultAsync(lineage, faulting => faulting.CommitFailure = mode);

        Assert.Null(thrown);
        Assert.NotNull(result);
        await using var verify = _fixture.CreateContext();
        if (landed)
        {
            Assert.True(result.IsSuccess);
            Assert.Empty(store.DeletedSealedFiles);
            Assert.Equal(result.Value.AttemptId, (await GrantAsync(verify, lineage.RunId)).ConsumedByAttemptId);
        }
        else
        {
            Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);
            Assert.Single(store.DeletedSealedFiles);
            await AssertGrantUnconsumedAsync(lineage.RunId);
        }
    }

    [Fact]
    public async Task A_raw_failure_at_a_late_read_removes_the_orphan_manifest_and_consumes_nothing()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();

        var (thrown, result, store) = await ClaimWithFaultAsync(
            lineage, faulting => faulting.BeforeSaveChanges = _ => throw new FaultInjectingDbContext.SimulatedDbException("Late read failed."));

        Assert.Null(thrown);
        Assert.Equal("attempts.persistence_failed", Assert.Single(result!.Errors).Code);
        Assert.Single(store.DeletedSealedFiles);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }

    [Fact]
    public async Task An_unanswered_durability_probe_keeps_the_manifest_instead_of_guessing()
    {
        var (lineage, _, _) = await SeedAuthorizedAsync();
        await using var dbContext = _fixture.CreateContext();
        var faulting = new FaultInjectingDbContext(dbContext) { CommitFailure = FaultInjectingDbContext.CommitFailureMode.BeforeCommit };
        var store = new RecordingArtifactStore();

        var result = await ClaimHandler(faulting, store: store, probe: new UnresolvedProbe()).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);

        Assert.Equal("attempts.persistence_unresolved", Assert.Single(result.Errors).Code);
        Assert.Empty(store.DeletedSealedFiles);
    }

    private sealed class UnresolvedProbe : IAttemptDurabilityProbe
    {
        public Task<AttemptDurabilityCheckResult> CheckAsync(Guid attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(AttemptDurabilityCheckResult.Unresolved);
    }

    // ---- Automatic caps are unchanged -------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depth_two_review_and_resolution_stay_refused_with_and_without_a_grant(bool authorized)
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        if (authorized)
        {
            Assert.True((await AuthorizeAsync(dbContext, lineage)).IsSuccess);
        }

        var impossibleReview = lineage.Seeder.AddReview(lineage.FinalProposal, AgentOutcome.Challenged);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var reviewEvidence = new CountingEvidenceReader();
        var store = new RecordingArtifactStore();

        var review = await new CreateClaudeCriticalReviewAttemptCommandHandler(
                dbContext, reviewEvidence, store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(new CreateClaudeCriticalReviewAttemptCommand(lineage.RunId, lineage.FinalProposal.Id), CancellationToken.None);
        var resolution = await new CreateChallengeResolutionAttemptCommandHandler(
                dbContext, new CountingEvidenceReader(), store, new FixedTimeProvider(Now), new AttemptDurabilityProbe(_fixture.Options))
            .HandleAsync(new CreateChallengeResolutionAttemptCommand(lineage.RunId, impossibleReview.Attempt.Id), CancellationToken.None);

        Assert.Equal(PlanningLineage.ExhaustedCode, Assert.Single(review.Errors).Code);
        Assert.Equal(PlanningLineage.ExhaustedCode, Assert.Single(resolution.Errors).Code);
        Assert.Equal(0, reviewEvidence.Captures);
        Assert.Equal(0, store.Seals);
    }

    [Fact]
    public async Task A_grant_never_makes_an_unrelated_plan_implementable_and_a_first_revision_stays_ordinary()
    {
        await using var dbContext = _fixture.CreateContext();
        var lineage = await SeedEscalatedLineageAsync(dbContext);
        Assert.True((await AuthorizeAsync(dbContext, lineage)).IsSuccess);
        var store = new RecordingArtifactStore();

        // The depth-one parent of the final plan is challenged by its own second review, so it is never implementable.
        var parent = await ClaimHandler(dbContext, store: store).HandleAsync(
            new CreateImplementationAttemptCommand(lineage.RunId, lineage.First.RevisedProposal.Id), CancellationToken.None);

        Assert.Equal("agent_attempts.plan_challenged", Assert.Single(parent.Errors).Code);
        await AssertGrantUnconsumedAsync(lineage.RunId);
    }
}
