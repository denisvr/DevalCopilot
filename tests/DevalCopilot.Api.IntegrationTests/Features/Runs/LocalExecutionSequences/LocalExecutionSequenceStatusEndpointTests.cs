using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060's bodyless status and stop over seeded durable facts: every state name, the manual-control fence, bounded step
/// receipts without any raw executable, argument, path, manifest, worker token or process identity, the fixed refusal of an inconsistent
/// tuple, and Stop around a claim. This class owns its host because seeded linked operations are real global owners that would make
/// unrelated arming reads refuse; sequences are therefore recorded directly through the Domain and never armed through the endpoint.</summary>
public sealed class LocalExecutionSequenceStatusEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string SettingsSentinel = "SENTINEL_SETTINGS_c4e2";

    private static readonly string Fingerprint = LocalExecutionSequenceApiSeed.Fingerprint;
    private static readonly string PlanInputsSentinel = new('9', 64);

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private static string Route(Guid runId) => $"/api/runs/{runId}/local-execution-sequence";

    private static string StopRoute(Guid runId, Guid sequenceId) => $"{Route(runId)}/{sequenceId}/stop";

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private async Task<(LocalExecutionSequenceApiScene Scene, Guid SequenceId)> SeedArmedAsync(TimeSpan? armedAgo = null)
    {
        var scene = await WithDbAsync(db => LocalExecutionSequenceApiSeed.SeedAsync(db));
        var armedAt = DateTimeOffset.UtcNow - (armedAgo ?? TimeSpan.FromMinutes(1));
        var sequenceId = await WithDbAsync(async db =>
        {
            var commands = await db.VerificationCommands.AsNoTracking()
                .Where(command => command.ProjectId == scene.ProjectId).OrderBy(command => command.CommandNumber).ToListAsync();
            var sequence = LocalExecutionSequence.Arm(
                Guid.NewGuid(), scene.RunId, scene.ProjectId, scene.WorkspaceId, scene.CheckpointId, Fingerprint, scene.PlanId,
                LocalExecutionPlanAuthority.AcceptedRoot, PlanInputsSentinel, new string('7', 64),
                $"devalcopilot.local-execution-sequence.settings.v1\n{SettingsSentinel}", commands.Count, 2, armedAt,
                LocalExecutionSequenceNumbers.Next());
            db.LocalExecutionSequences.Add(sequence);
            for (var index = 0; index < commands.Count; index++)
            {
                db.LocalExecutionSequenceRecipes.Add(LocalExecutionSequenceRecipe.Pin(Guid.NewGuid(), sequence.Id, index, commands[index]));
            }

            db.Events.Add(RunEvent.Record(
                Guid.NewGuid(), scene.RunId, null, RunEventType.LocalExecutionSequenceArmed, ParticipantIdentity.ForHuman(), "{}", armedAt));
            await db.SaveChangesAsync();
            return sequence.Id;
        });
        return (scene, sequenceId);
    }

    private Task MutateAsync(Guid sequenceId, Action<LocalExecutionSequence> change) => WithDbAsync(async db =>
    {
        var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId);
        change(sequence);
        await db.SaveChangesAsync();
        return 0;
    });

    private static bool Ended(
        LocalExecutionSequence sequence, LocalExecutionSequenceState end, string reason, DateTimeOffset now, Guid report, Guid? review, Guid? escalation)
    {
        sequence.End(end, reason, null, now, report, review, escalation);
        return true;
    }

    private enum OperationShape
    {
        Running,
        ExitRecorded,
        NoExitEvidence,
    }

    /// <summary>Reserves, claims and (when asked) concludes the implementation step of a sequence with a real linked Agent attempt.</summary>
    private async Task<(Guid StepId, Guid AttemptId, Guid OwnerToken, Guid ReservedOperationId)> LinkImplementationAsync(
        LocalExecutionSequenceApiScene scene, Guid sequenceId, OperationShape shape, bool conclude)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-30);
        var operationId = Guid.NewGuid();
        var ownerToken = Guid.NewGuid();
        var step = LocalExecutionSequenceStep.Reserve(
            Guid.NewGuid(), sequenceId, 0, LocalExecutionStepKind.Implementation, 0, null, null, scene.CheckpointId, Fingerprint, null, null,
            operationId, ownerToken, now);
        await WithDbAsync(async db =>
        {
            var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId);
            sequence.BeginPreparing(now);
            db.LocalExecutionSequenceSteps.Add(step);
            await db.SaveChangesAsync();
            return 0;
        });
        await WithDbAsync(async db =>
        {
            var attempt = Attempt.ClaimAgentImplementation(
                operationId, scene.RunId, 3, scene.WorkspaceId, scene.CheckpointId, Fingerprint, Guid.NewGuid(), TimeSpan.FromMinutes(10),
                262144, 524288, now, 3);
            attempt.MarkAgentDispatched(now);
            if (shape == OperationShape.ExitRecorded)
            {
                attempt.CompleteAgent(
                    AgentOutcome.ProviderInvocationFailed, null, now,
                    processEvidence: AgentProcessExecutionEvidence.Create(ProcessOutcome.Exited, 1, TimeSpan.FromSeconds(2)));
            }
            else if (shape == OperationShape.NoExitEvidence)
            {
                attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, now);
            }

            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
            return 0;
        });
        await WithDbAsync(async db =>
        {
            var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId);
            var stored = await db.LocalExecutionSequenceSteps.SingleAsync(candidate => candidate.Id == step.Id);
            stored.MarkClaimed(operationId, now, DateTimeOffset.UtcNow.AddMinutes(30));
            sequence.MarkAwaitingResult(now);
            await db.SaveChangesAsync();
            if (conclude)
            {
                stored.Conclude(now);
                await db.SaveChangesAsync();
            }

            return 0;
        });
        return (step.Id, operationId, ownerToken, operationId);
    }

    // ---- None and the armed closed graph ----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_status_of_an_unarmed_run_is_None_with_a_released_fence_and_a_string_event_sequence()
    {
        var scene = await WithDbAsync(db => LocalExecutionSequenceApiSeed.SeedAsync(db));
        using var client = Client();

        var response = await client.GetAsync(Route(scene.RunId));
        var body = await BodyAsync(response);
        var unknown = await client.GetAsync(Route(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("None", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sequence").ValueKind);
        Assert.Equal("Released", body.GetProperty("manualFence").GetString());
        Assert.Equal(scene.RunId, body.GetProperty("runId").GetGuid());
        Assert.Equal(JsonValueKind.String, body.GetProperty("latestEventSequence").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.NotFoundCode, await ErrorCodeAsync(unknown));
    }

    [Fact]
    public async Task The_status_of_an_armed_run_is_the_closed_graph_with_the_pinned_tuple_and_a_blocked_fence()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        using var client = Client();

        var body = await BodyAsync(await client.GetAsync(Route(scene.RunId)));

        Assert.Equal("Armed", body.GetProperty("status").GetString());
        Assert.Equal("Blocked", body.GetProperty("manualFence").GetString());
        var sequence = body.GetProperty("sequence");
        Assert.Equal(sequenceId, sequence.GetProperty("sequenceId").GetGuid());
        Assert.Equal(scene.PlanId, sequence.GetProperty("planProposalMessageId").GetGuid());
        Assert.Equal("AcceptedRoot", sequence.GetProperty("planAuthority").GetString());
        Assert.Equal(scene.WorkspaceId, sequence.GetProperty("workspaceId").GetGuid());
        Assert.Equal(scene.CheckpointId, sequence.GetProperty("checkpointId").GetGuid());
        Assert.Equal(scene.CheckpointId, sequence.GetProperty("currentCheckpointId").GetGuid());
        Assert.Equal(Fingerprint, sequence.GetProperty("fingerprintSha256").GetString());
        Assert.Equal(Fingerprint, sequence.GetProperty("currentFingerprintSha256").GetString());
        Assert.Equal("Armed", sequence.GetProperty("state").GetString());
        Assert.Equal("Armed", sequence.GetProperty("recordedState").GetString());
        Assert.False(sequence.GetProperty("observedBeforePersisted").GetBoolean());
        Assert.Equal("wait_or_stop_after_current_operation", sequence.GetProperty("nextActionCode").GetString());
        Assert.Equal("Wait, or choose Stop after current operation.", sequence.GetProperty("nextActionText").GetString());
        Assert.Equal(JsonValueKind.Null, sequence.GetProperty("reasonCode").ValueKind);
        Assert.Equal("1", sequence.GetProperty("revision").GetString());
        Assert.Equal(2, sequence.GetProperty("recipeCount").GetInt32());
        Assert.Equal(12, sequence.GetProperty("maximumSteps").GetInt32());
        Assert.Equal(2, sequence.GetProperty("correctionAllowance").GetInt32());
        Assert.Equal(0, sequence.GetProperty("correctionsClaimed").GetInt32());
        Assert.Equal(0, sequence.GetProperty("passesStarted").GetInt32());
        Assert.Equal(0, sequence.GetProperty("agentClaims").GetInt32());
        Assert.False(sequence.GetProperty("awaitingReconciliation").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sequence.GetProperty("outstandingOperationId").ValueKind);
        Assert.Equal(0, sequence.GetProperty("steps").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, sequence.GetProperty("final").ValueKind);
        Assert.Equal(JsonValueKind.Null, sequence.GetProperty("stopRequestedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, sequence.GetProperty("endedAtUtc").ValueKind);
        var armedAt = DateTimeOffset.Parse(sequence.GetProperty("armedAtUtc").GetString()!);
        Assert.Equal(TimeSpan.FromHours(2), DateTimeOffset.Parse(sequence.GetProperty("advancementDeadlineUtc").GetString()!) - armedAt);
        Assert.Equal(
            new HashSet<string>
            {
                "sequenceId", "planProposalMessageId", "planAuthority", "workspaceId", "checkpointId", "fingerprintSha256", "currentCheckpointId",
                "currentFingerprintSha256", "state", "recordedState", "observedBeforePersisted", "reasonCode", "reasonText", "detailCode",
                "nextActionCode", "nextActionText", "revision", "recipeCount", "maximumSteps", "correctionAllowance", "correctionsClaimed",
                "passesStarted", "agentClaims", "armedAtUtc", "advancementDeadlineUtc", "updatedAtUtc", "stopRequestedAtUtc", "endedAtUtc",
                "awaitingReconciliation", "outstandingOperationId", "steps", "final", "admissionMode", "queue",
            },
            sequence.EnumerateObject().Select(property => property.Name).ToHashSet());
    }

    [Fact]
    public async Task An_elapsed_advancement_deadline_reads_as_attention_before_it_is_persisted()
    {
        var (scene, _) = await SeedArmedAsync(armedAgo: TimeSpan.FromHours(3));
        using var client = Client();

        var body = await BodyAsync(await client.GetAsync(Route(scene.RunId)));

        Assert.Equal("NeedsAttention", body.GetProperty("status").GetString());
        var sequence = body.GetProperty("sequence");
        Assert.Equal("NeedsAttention", sequence.GetProperty("state").GetString());
        Assert.Equal("Armed", sequence.GetProperty("recordedState").GetString());
        Assert.True(sequence.GetProperty("observedBeforePersisted").GetBoolean());
        Assert.Equal("advancement_deadline_elapsed", sequence.GetProperty("reasonCode").GetString());
        Assert.Equal(
            "The two-hour advancement deadline elapsed; no further operation is started.", sequence.GetProperty("reasonText").GetString());
        Assert.Equal(LocalExecutionSequenceState.Armed, await WithDbAsync(
            async db => (await db.LocalExecutionSequences.AsNoTracking().SingleAsync(candidate => candidate.RunId == scene.RunId)).State));
    }

    // ---- every state name and the manual fence ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Armed", "Blocked", "wait_or_stop_after_current_operation")]
    [InlineData("PreparingStep", "Blocked", "wait_or_stop_after_current_operation")]
    [InlineData("AwaitingStepResult", "Blocked", "wait_or_stop_after_current_operation")]
    [InlineData("Stopping", "Blocked", "wait_for_current_operation_to_finish")]
    [InlineData("ReadyForHumanApproval", "Released", "review_and_decide_on_human_approval_separately")]
    [InlineData("HumanEscalation", "Released", "decide_on_the_recorded_escalation")]
    [InlineData("Stopped", "Released", "continue_with_explicit_manual_work")]
    [InlineData("NeedsAttention", "Released", "inspect_and_reconcile")]
    [InlineData("Interrupted", "Released", "inspect_and_reconcile")]
    public async Task Every_state_name_is_reported_with_its_fixed_next_action_and_manual_fence(string state, string fence, string nextAction)
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        var now = DateTimeOffset.UtcNow;
        var linkImplementation = state is "AwaitingStepResult" or "Stopping";
        if (linkImplementation)
        {
            await LinkImplementationAsync(scene, sequenceId, OperationShape.Running, conclude: false);
        }

        switch (state)
        {
            case "PreparingStep":
                await WithDbAsync(async db =>
                {
                    var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId);
                    sequence.BeginPreparing(now);
                    db.LocalExecutionSequenceSteps.Add(LocalExecutionSequenceStep.Reserve(
                        Guid.NewGuid(), sequenceId, 0, LocalExecutionStepKind.Implementation, 0, null, null, scene.CheckpointId, Fingerprint,
                        null, null, Guid.NewGuid(), Guid.NewGuid(), now));
                    await db.SaveChangesAsync();
                    return 0;
                });
                break;
            case "Stopping":
                await MutateAsync(sequenceId, sequence => sequence.BeginStopping(now));
                break;
            case "ReadyForHumanApproval":
            case "HumanEscalation":
                // Existing rows the foreign keys accept but that are no linked review step and no Escalation message: unproved identities.
                var unprovedAttempt = await WithDbAsync(db => db.Attempts.AsNoTracking()
                    .Where(attempt => attempt.RunId == scene.RunId).Select(attempt => attempt.Id).FirstAsync());
                var unprovedMessage = await WithDbAsync(db => db.CollaborationMessages.AsNoTracking()
                    .Where(message => message.RunId == scene.RunId && message.Type == CollaborationMessageType.Acceptance)
                    .Select(message => message.Id).SingleAsync());
                await MutateAsync(sequenceId, sequence => _ = state == "ReadyForHumanApproval"
                    ? Ended(sequence, LocalExecutionSequenceState.ReadyForHumanApproval, LocalExecutionSequenceReason.ReviewApproved, now, scene.PlanId, unprovedAttempt, null)
                    : Ended(sequence, LocalExecutionSequenceState.HumanEscalation, LocalExecutionSequenceReason.AllowanceReached, now, scene.PlanId, null, unprovedMessage));
                break;
            case "Stopped":
                await MutateAsync(sequenceId, sequence => sequence.End(
                    LocalExecutionSequenceState.Stopped, LocalExecutionSequenceReason.StoppedBeforeClaim, null, now));
                break;
            case "NeedsAttention":
                await MutateAsync(sequenceId, sequence => sequence.End(
                    LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationFailed, "provider_invocation_failed", now));
                break;
            case "Interrupted":
                await MutateAsync(sequenceId, sequence => sequence.End(
                    LocalExecutionSequenceState.Interrupted, LocalExecutionSequenceReason.HostRestarted, null, now));
                break;
        }

        using var client = Client();
        var body = await BodyAsync(await client.GetAsync(Route(scene.RunId)));

        Assert.Equal(state, body.GetProperty("status").GetString());
        Assert.Equal(fence, body.GetProperty("manualFence").GetString());
        var sequence = body.GetProperty("sequence");
        Assert.Equal(state, sequence.GetProperty("state").GetString());
        Assert.Equal(state, sequence.GetProperty("recordedState").GetString());
        Assert.Equal(nextAction, sequence.GetProperty("nextActionCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(sequence.GetProperty("nextActionText").GetString()));
        Assert.Equal(
            state is "ReadyForHumanApproval" or "HumanEscalation" or "Stopped" or "NeedsAttention" or "Interrupted",
            sequence.GetProperty("endedAtUtc").ValueKind == JsonValueKind.String);
        if (sequence.GetProperty("reasonCode").ValueKind == JsonValueKind.String)
        {
            Assert.False(string.IsNullOrWhiteSpace(sequence.GetProperty("reasonText").GetString()));
        }

        if (state == "NeedsAttention")
        {
            Assert.Equal("provider_invocation_failed", sequence.GetProperty("detailCode").GetString());
        }

        // Final references are disclosed only when proved from durable rows; unproved identities are never shown.
        Assert.Equal(JsonValueKind.Null, sequence.GetProperty("final").ValueKind);
    }

    [Fact]
    public async Task An_escalation_that_answers_no_linked_execution_report_is_not_disclosed_as_final_evidence()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        var escalationId = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            db.CollaborationMessages.Add(CollaborationMessage.Record(
                escalationId, scene.RunId, null, CollaborationMessage.ProtocolVersionOne, ParticipantIdentity.ForOrchestrator(),
                ParticipantIdentity.ForHuman(), CollaborationMessageType.Escalation, scene.PlanId, "Local execution cycle requires a human decision.",
                JsonSerializer.Serialize(new
                {
                    unresolvedDecision = "The authorized local execution cycle has unresolved findings.",
                    options = "Inspect the recorded evidence and decide on an explicit manual next action. This cycle cannot continue.",
                    consequences = "No further operation is authorized by this sequence; the run remains awaiting human action.",
                    evidence = "The authorized automatic correction allowance was reached.",
                    recommendedChoice = "Inspect the evidence before requesting any manual continuation.",
                }),
                CollaborationMessageProvenance.HostConstructed, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        await MutateAsync(sequenceId, sequence => sequence.End(
            LocalExecutionSequenceState.HumanEscalation, LocalExecutionSequenceReason.AllowanceReached, null, DateTimeOffset.UtcNow,
            scene.PlanId, null, escalationId));
        using var client = Client();

        var body = await BodyAsync(await client.GetAsync(Route(scene.RunId)));

        // ADR-0060 review 1 (LCO-R2): the recorded outcome stays, but a final reference is proved from the actual evidence. This
        // escalation answers a Proposal that no step names as its linked report, so nothing is disclosed. The positive proof of a
        // real host exhaustion and diagnosis escalation is in the hosted and Application final-evidence tests.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sequence").GetProperty("final").ValueKind);
        Assert.Equal("HumanEscalation", body.GetProperty("status").GetString());
        Assert.Equal("allowance_reached", body.GetProperty("sequence").GetProperty("reasonCode").GetString());
    }

    // ---- bounded step receipts and non-disclosure -----------------------------------------------------------------------------

    [Fact]
    public async Task Step_receipts_are_bounded_and_disclose_no_executable_argument_path_manifest_token_or_process_identity()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        var linked = await LinkImplementationAsync(scene, sequenceId, OperationShape.ExitRecorded, conclude: true);
        var reservedVerification = Guid.NewGuid();
        var verificationOwner = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId);
            sequence.BeginPreparing(DateTimeOffset.UtcNow);
            db.LocalExecutionSequenceSteps.Add(LocalExecutionSequenceStep.Reserve(
                Guid.NewGuid(), sequenceId, 1, LocalExecutionStepKind.Verification, 1, 1, linked.StepId, scene.CheckpointId, Fingerprint,
                scene.PlanId, null, reservedVerification, verificationOwner, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });
        using var client = Client();

        var response = await client.GetAsync(Route(scene.RunId));
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        var sequence = document.RootElement.GetProperty("sequence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("PreparingStep", sequence.GetProperty("state").GetString());
        Assert.Equal(1, sequence.GetProperty("agentClaims").GetInt32());
        Assert.Equal(1, sequence.GetProperty("passesStarted").GetInt32());
        var steps = sequence.GetProperty("steps");
        Assert.Equal(2, steps.GetArrayLength());
        var implementation = steps[0];
        Assert.Equal(0, implementation.GetProperty("ordinal").GetInt32());
        Assert.Equal("Implementation", implementation.GetProperty("kind").GetString());
        Assert.Equal("Concluded", implementation.GetProperty("state").GetString());
        Assert.Equal(linked.AttemptId, implementation.GetProperty("operationId").GetGuid());
        Assert.Equal("Failed", implementation.GetProperty("operationStatus").GetString());
        Assert.Equal("ProviderInvocationFailed", implementation.GetProperty("operationOutcome").GetString());
        Assert.True(implementation.GetProperty("dispatched").GetBoolean());
        Assert.True(implementation.GetProperty("exitRecorded").GetBoolean());
        Assert.False(implementation.GetProperty("quarantined").GetBoolean());
        Assert.Equal(JsonValueKind.Null, implementation.GetProperty("recipeOrdinal").ValueKind);
        var verification = steps[1];
        Assert.Equal("Verification", verification.GetProperty("kind").GetString());
        Assert.Equal("Reserved", verification.GetProperty("state").GetString());
        Assert.Equal(1, verification.GetProperty("passNumber").GetInt32());
        Assert.Equal(1, verification.GetProperty("recipeOrdinal").GetInt32());
        Assert.Equal("Unit tests 2", verification.GetProperty("recipeName").GetString());
        Assert.Equal(JsonValueKind.Null, verification.GetProperty("operationId").ValueKind);
        Assert.Equal(JsonValueKind.Null, verification.GetProperty("dispatched").ValueKind);
        Assert.Equal(
            new HashSet<string>
            {
                "stepId", "ordinal", "kind", "passNumber", "recipeOrdinal", "recipeName", "state", "reasonCode", "operationId", "operationStatus",
                "operationOutcome", "dispatched", "exitRecorded", "quarantined", "ownershipReleased", "preparationReservedAtUtc", "preparationDeadlineUtc", "claimedAtUtc",
                "resultDeadlineUtc",
            },
            verification.EnumerateObject().Select(property => property.Name).ToHashSet());

        var readable = text.Replace("\\\\", "\\");
        foreach (var sentinel in new[]
                 {
                     "SENTINEL_EXECUTABLE", LocalExecutionSequenceApiSeed.SentinelArgument, scene.WorkspacePath, scene.RepositoryPath, SettingsSentinel,
                     PlanInputsSentinel, new string('7', 64), linked.OwnerToken.ToString(), verificationOwner.ToString(), reservedVerification.ToString(),
                     "devalcopilot.local-execution-sequence",
                 })
        {
            Assert.DoesNotContain(sentinel, readable, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_dispatched_operation_without_exit_evidence_stays_quarantined_and_no_recovery_is_offered()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        await LinkImplementationAsync(scene, sequenceId, OperationShape.NoExitEvidence, conclude: true);
        await MutateAsync(sequenceId, sequence => sequence.End(
            LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.ExitEvidenceMissing, null, DateTimeOffset.UtcNow));
        using var client = Client();

        var body = await BodyAsync(await client.GetAsync(Route(scene.RunId)));

        Assert.Equal("Blocked", body.GetProperty("manualFence").GetString());
        var sequence = body.GetProperty("sequence");
        Assert.True(sequence.GetProperty("awaitingReconciliation").GetBoolean());
        Assert.Equal("no_supported_recovery_action_in_this_version", sequence.GetProperty("nextActionCode").GetString());
        Assert.Equal("No automatic recovery. A separate Human release is possible only with recorded original-host termination evidence; without it there is no supported recovery action.", sequence.GetProperty("nextActionText").GetString());
        var step = sequence.GetProperty("steps")[0];
        Assert.True(step.GetProperty("quarantined").GetBoolean());
        Assert.False(step.GetProperty("ownershipReleased").GetBoolean());
        Assert.False(step.GetProperty("exitRecorded").GetBoolean());
        Assert.Equal(step.GetProperty("operationId").GetGuid(), sequence.GetProperty("outstandingOperationId").GetGuid());
    }

    [Fact]
    public async Task A_running_linked_operation_is_outstanding_and_a_proved_exit_releases_the_fence_after_the_end()
    {
        var (runningScene, runningSequence) = await SeedArmedAsync();
        var running = await LinkImplementationAsync(runningScene, runningSequence, OperationShape.Running, conclude: false);
        var (exitedScene, exitedSequence) = await SeedArmedAsync();
        await LinkImplementationAsync(exitedScene, exitedSequence, OperationShape.ExitRecorded, conclude: true);
        await MutateAsync(exitedSequence, sequence => sequence.End(
            LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationFailed, null, DateTimeOffset.UtcNow));
        using var client = Client();

        var live = await BodyAsync(await client.GetAsync(Route(runningScene.RunId)));
        var released = await BodyAsync(await client.GetAsync(Route(exitedScene.RunId)));

        Assert.Equal("Blocked", live.GetProperty("manualFence").GetString());
        var liveSequence = live.GetProperty("sequence");
        Assert.Equal(running.AttemptId, liveSequence.GetProperty("outstandingOperationId").GetGuid());
        var liveStep = liveSequence.GetProperty("steps")[0];
        Assert.Equal("Running", liveStep.GetProperty("operationStatus").GetString());
        Assert.True(liveStep.GetProperty("dispatched").GetBoolean());
        Assert.False(liveStep.GetProperty("exitRecorded").GetBoolean());
        Assert.False(liveStep.GetProperty("quarantined").GetBoolean());
        Assert.Equal("Released", released.GetProperty("manualFence").GetString());
        Assert.False(released.GetProperty("sequence").GetProperty("awaitingReconciliation").GetBoolean());
    }

    // ---- inconsistent tuples ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_step_sequence_with_a_gap_is_refused_with_the_fixed_persistence_failure_instead_of_being_shown()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        await WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId);
            sequence.BeginPreparing(now);
            var first = LocalExecutionSequenceStep.Reserve(
                Guid.NewGuid(), sequenceId, 0, LocalExecutionStepKind.Implementation, 0, null, null, scene.CheckpointId, Fingerprint, null, null,
                Guid.NewGuid(), Guid.NewGuid(), now);
            db.LocalExecutionSequenceSteps.Add(first);
            await db.SaveChangesAsync();
            first.Revoke(LocalExecutionSequenceReason.PreparationRefused, now);
            await db.SaveChangesAsync();
            db.LocalExecutionSequenceSteps.Add(LocalExecutionSequenceStep.Reserve(
                Guid.NewGuid(), sequenceId, 2, LocalExecutionStepKind.Verification, 1, 0, first.Id, scene.CheckpointId, Fingerprint, scene.PlanId, null,
                Guid.NewGuid(), Guid.NewGuid(), now));
            await db.SaveChangesAsync();
            return 0;
        });
        using var client = Client();

        var response = await client.GetAsync(Route(scene.RunId));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.PersistenceFailedCode, await ErrorCodeAsync(response));
        Assert.DoesNotContain("steps", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_status_is_bodyless_and_queryless()
    {
        var (scene, _) = await SeedArmedAsync();
        using var client = Client();

        var query = await client.GetAsync($"{Route(scene.RunId)}?x=1");
        using var withBody = new HttpRequestMessage(HttpMethod.Get, Route(scene.RunId)) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        var body = await client.SendAsync(withBody);

        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.InvalidRequestCode, await ErrorCodeAsync(query));
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);
        Assert.Equal(LocalExecutionSequenceErrors.BodyNotAllowedCode, await ErrorCodeAsync(body));
    }

    // ---- stop around a claim --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stop_before_a_reserved_step_is_claimed_revokes_it_and_consumes_nothing()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        var reservedOperation = Guid.NewGuid();
        await WithDbAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var sequence = await db.LocalExecutionSequences.SingleAsync(candidate => candidate.Id == sequenceId);
            sequence.BeginPreparing(now);
            db.LocalExecutionSequenceSteps.Add(LocalExecutionSequenceStep.Reserve(
                Guid.NewGuid(), sequenceId, 0, LocalExecutionStepKind.Implementation, 0, null, null, scene.CheckpointId, Fingerprint, null, null,
                reservedOperation, Guid.NewGuid(), now));
            await db.SaveChangesAsync();
            return 0;
        });
        using var client = Client();

        var response = await client.PostAsync(StopRoute(scene.RunId, sequenceId), null);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Stopped", body.GetProperty("state").GetString());
        Assert.Equal("stopped_before_claim", body.GetProperty("reasonCode").GetString());
        Assert.True(body.GetProperty("changed").GetBoolean());
        var step = await WithDbAsync(db => db.LocalExecutionSequenceSteps.AsNoTracking().SingleAsync(candidate => candidate.SequenceId == sequenceId));
        Assert.Equal(LocalExecutionSequenceStepState.Revoked, step.State);
        Assert.Null(step.OperationId);
        Assert.False(await WithDbAsync(db => db.Attempts.AsNoTracking().AnyAsync(attempt => attempt.Id == reservedOperation)));
        Assert.Equal(2, await WithDbAsync(db => db.Attempts.AsNoTracking().CountAsync(attempt => attempt.RunId == scene.RunId)));
    }

    [Fact]
    public async Task Stop_after_a_claim_is_Stopping_leaves_the_claimed_operation_untouched_and_is_idempotent()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        var linked = await LinkImplementationAsync(scene, sequenceId, OperationShape.Running, conclude: false);
        using var client = Client();

        var first = await client.PostAsync(StopRoute(scene.RunId, sequenceId), null);
        var firstBody = await BodyAsync(first);
        var second = await client.PostAsync(StopRoute(scene.RunId, sequenceId), null);
        var secondBody = await BodyAsync(second);
        var status = await BodyAsync(await client.GetAsync(Route(scene.RunId)));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("Stopping", firstBody.GetProperty("state").GetString());
        Assert.True(firstBody.GetProperty("changed").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Stopping", secondBody.GetProperty("state").GetString());
        Assert.False(secondBody.GetProperty("changed").GetBoolean());
        Assert.Equal(firstBody.GetProperty("revision").GetString(), secondBody.GetProperty("revision").GetString());
        Assert.Equal("Stopping", status.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, status.GetProperty("sequence").GetProperty("stopRequestedAtUtc").ValueKind);
        var step = await WithDbAsync(db => db.LocalExecutionSequenceSteps.AsNoTracking().SingleAsync(candidate => candidate.SequenceId == sequenceId));
        Assert.Equal(LocalExecutionSequenceStepState.Claimed, step.State);
        var attempt = await WithDbAsync(db => db.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == linked.AttemptId));
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(1, await WithDbAsync(db => db.Events.AsNoTracking().CountAsync(
            runEvent => runEvent.RunId == scene.RunId && runEvent.EventType == RunEventType.LocalExecutionSequenceStopRequested)));
    }

    [Fact]
    public async Task Stopping_an_ended_sequence_returns_it_unchanged()
    {
        var (scene, sequenceId) = await SeedArmedAsync();
        await MutateAsync(sequenceId, sequence => sequence.End(
            LocalExecutionSequenceState.NeedsAttention, LocalExecutionSequenceReason.OperationFailed, null, DateTimeOffset.UtcNow));
        var before = await WithDbAsync(db => db.LocalExecutionSequences.AsNoTracking().SingleAsync(candidate => candidate.Id == sequenceId));
        using var client = Client();

        var response = await client.PostAsync(StopRoute(scene.RunId, sequenceId), null);
        var body = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("NeedsAttention", body.GetProperty("state").GetString());
        Assert.Equal("operation_failed", body.GetProperty("reasonCode").GetString());
        Assert.False(body.GetProperty("changed").GetBoolean());
        Assert.Equal(before.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), body.GetProperty("revision").GetString());
        Assert.Equal(0, await WithDbAsync(db => db.Events.AsNoTracking().CountAsync(
            runEvent => runEvent.RunId == scene.RunId && runEvent.EventType == RunEventType.LocalExecutionSequenceStopRequested)));
    }
}
