using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

public sealed class MigrationTests(SqliteFileFixture fixture) : IClassFixture<SqliteFileFixture>
{
    [Fact]
    public async Task New_migration_backfills_existing_runs_to_two_without_changing_existing_values()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-budget-backfill-{Guid.NewGuid():N}.db");
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        try
        {
            await using (var previousContext = CreateContext(databasePath))
            {
                await previousContext.Database.MigrateAsync("AddAttemptVerificationEvidence");
                var project = Project.Register(projectId, "Budget backfill", $@"C:\repos\budget-backfill-{Guid.NewGuid():N}", now);
                previousContext.Projects.Add(project);
                await previousContext.SaveChangesAsync();
                await previousContext.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO runs
                           (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipant,
                            CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                       VALUES
                           ({runId}, {projectId}, {7}, {"Pre-existing objective"},
                            {nameof(RunLifecycle.Running)}, {nameof(RunStage.Critique)}, {nameof(ParticipantKind.Orchestrator)},
                            {now}, {now.AddMinutes(1)}, {12.5d})");
            }

            await using (var upgradedContext = CreateContext(databasePath))
            {
                await upgradedContext.Database.MigrateAsync();
            }

            await using var reopenedContext = CreateContext(databasePath);
            var run = await reopenedContext.Runs.SingleAsync(candidate => candidate.Id == runId);
            Assert.Equal(2, run.MaximumReviewCorrectionAttempts);
            Assert.Equal(projectId, run.ProjectId);
            Assert.Equal(7, run.ExecutionNumber);
            Assert.Equal("Pre-existing objective", run.Objective);
            Assert.Equal(RunLifecycle.Running, run.Lifecycle);
            Assert.Equal(RunStage.Critique, run.Stage);
            Assert.Equal(12.5, run.AccumulatedAutonomousSeconds);
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task AddAgentAssignmentFacts_preserves_historical_provider_and_leaves_new_assignment_facts_unknown()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"devalcopilot-agent-assignment-migration-{Guid.NewGuid():N}.db");
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var historicalAttemptId = Guid.NewGuid();

        try
        {
            await using (var previousContext = CreateContext(databasePath))
            {
                await previousContext.Database.MigrateAsync("AddReviewCorrectionBudgetAndEscalations");
                var project = Project.Register(projectId, "Assignment migration", $@"C:\repos\assignment-migration-{Guid.NewGuid():N}", now);
                previousContext.Projects.Add(project);
                await previousContext.SaveChangesAsync();
                await previousContext.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO runs
                           (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipantKind,
                            CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                       VALUES
                           ({runId}, {projectId}, {1}, {"Preserve provider facts"},
                            {nameof(RunLifecycle.Created)}, {nameof(RunStage.Intake)}, {nameof(ParticipantKind.None)},
                            {now}, {now}, {0d})");
                await previousContext.Database.ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentProvider)
                       VALUES ({historicalAttemptId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {now}, {""}, {nameof(AgentProvider.ClaudeCode)})");
            }

            await using (var upgradedContext = CreateContext(databasePath))
            {
                await upgradedContext.Database.MigrateAsync();

                var newAttempt = Attempt.ClaimAgentImplementationWithAssignment(
                    Guid.NewGuid(), runId, 2, Guid.NewGuid(), Guid.NewGuid(), "sha256:migration", Guid.NewGuid(),
                    TimeSpan.FromMinutes(20), 1024, 2048, now, "claude-model", "balanced",
                    AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v1");
                upgradedContext.Attempts.Add(newAttempt);
                await upgradedContext.SaveChangesAsync();
            }

            await using var reopenedContext = CreateContext(databasePath);
            var historicalAttempt = await reopenedContext.Attempts.FindAsync(historicalAttemptId);
            Assert.NotNull(historicalAttempt);
            Assert.Equal(AgentProvider.ClaudeCode, historicalAttempt.AgentProvider);
            Assert.Null(historicalAttempt.AgentRequestedModel);
            Assert.Null(historicalAttempt.AgentObservedModel);
            Assert.Null(historicalAttempt.AgentRequestedEffort);
            Assert.Null(historicalAttempt.AgentObservedEffort);
            Assert.Null(historicalAttempt.AgentPermissionProfile);
            Assert.Null(historicalAttempt.AgentAdapterContractVersion);
            Assert.Equal(AgentPermissionProfile.Unknown, historicalAttempt.GetAssignmentSnapshot()!.PermissionProfile);

            var roundTrippedAttempt = await reopenedContext.Attempts.SingleAsync(attempt => attempt.AgentRequestedModel == "claude-model");
            Assert.Equal(AgentProvider.ClaudeCode, roundTrippedAttempt.AgentProvider);
            Assert.Equal("claude-model", roundTrippedAttempt.AgentRequestedModel);
            Assert.Equal("balanced", roundTrippedAttempt.AgentRequestedEffort);
            Assert.Equal(AgentPermissionProfile.WorkspaceEditOnly, roundTrippedAttempt.AgentPermissionProfile);
            Assert.Equal("claude-implementation-v1", roundTrippedAttempt.AgentAdapterContractVersion);
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={databasePath}"));
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_project_run_attempt_and_event()
    {
        var now = DateTimeOffset.UtcNow;

        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\migration-test", DateTimeOffset.UtcNow);
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Prove the schema", now);
            context.Runs.Add(run);
            await context.SaveChangesAsync();
            Assert.Equal(2, run.MaximumReviewCorrectionAttempts);

            run.Claim(now);
            var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, attemptNumber: 1, now);
            context.Attempts.Add(attempt);
            await context.SaveChangesAsync();

            var runEvent = RunEvent.Record(Guid.NewGuid(), run.Id, attempt.Id, RunEventType.RunStarted, ParticipantIdentity.ForOrchestrator(), "{}", now);
            context.Events.Add(runEvent);
            await context.SaveChangesAsync();

            Assert.True(runEvent.Sequence > 0);
        }
    }

    private static DevalCopilotDbContext CreateContext(string databasePath) =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options);

    [Fact]
    public async Task Migrate_applied_twice_is_idempotent()
    {
        await using var first = fixture.CreateContext();
        await first.Database.MigrateAsync();

        await using var second = fixture.CreateContext();
        await second.Database.MigrateAsync();

        var appliedMigrations = (await second.Database.GetAppliedMigrationsAsync())
            .Select(id => id.Split('_').Last())
            .ToArray();
        Assert.Contains("InitialCreate", appliedMigrations);
        Assert.Contains("AddProcessAttempts", appliedMigrations);
        Assert.Contains("AddProcessDispatchMarker", appliedMigrations);
        Assert.Contains("AddHostCapabilitySnapshots", appliedMigrations);
        Assert.Contains("AddCollaborationMessages", appliedMigrations);
        Assert.Contains("AddProviderLaunchTargets", appliedMigrations);
        Assert.Contains("AddCodexPlanningAttempts", appliedMigrations);
        Assert.Contains("AgentAttemptCorrectionRound1", appliedMigrations);
        Assert.Contains("AddClaudeCriticalReviewAttempts", appliedMigrations);
        Assert.Contains("AddAttemptInputMessages", appliedMigrations);
        Assert.Contains("AddNeutralParticipantIdentity", appliedMigrations);
        Assert.Contains("AddReviewCorrectionBudgetAndEscalations", appliedMigrations);
        Assert.Contains("AddAgentAssignmentFacts", appliedMigrations);
    }

    [Fact]
    public async Task Migrate_backfills_provider_named_participants_without_inventing_unknown_roles()
    {
        var now = DateTimeOffset.UtcNow;
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var attemptBoundEventId = Guid.NewGuid();
        var attemptlessEventId = Guid.NewGuid();
        var orchestratorEventId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync("AddAttemptVerificationEvidence");

            var project = Project.Register(
                Guid.NewGuid(),
                "Participant migration",
                $@"C:\repos\participant-migration-{Guid.NewGuid():N}",
                now);
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO runs
                       (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipant,
                        CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
                   VALUES
                       ({runId}, {project.Id}, {1}, {"Preserve participant identity"},
                        {nameof(RunLifecycle.Running)}, {nameof(RunStage.Critique)}, {"Codex"},
                        {now}, {now}, {0d})");

            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts
                       (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments,
                        AgentRole, AgentProvider)
                   VALUES
                       ({attemptId}, {runId}, {1}, {nameof(AttemptKind.Agent)},
                        {nameof(AttemptStatus.Completed)}, {now}, {""},
                        {nameof(AgentRole.CriticalReviewer)}, {nameof(AgentProvider.ClaudeCode)})");

            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO events (Id, RunId, AttemptId, EventType, Actor, PayloadJson, OccurredAtUtc)
                   VALUES
                       ({attemptBoundEventId}, {runId}, {attemptId}, {nameof(RunEventType.AgentAttemptCompleted)},
                        {"Claude"}, {"{}"}, {now}),
                       ({attemptlessEventId}, {runId}, NULL, {nameof(RunEventType.RunCompleted)},
                        {"Codex"}, {"{}"}, {now}),
                       ({orchestratorEventId}, {runId}, NULL, {nameof(RunEventType.RunStarted)},
                        {nameof(ParticipantKind.Orchestrator)}, {"{}"}, {now})");

            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO collaboration_messages
                       (Id, RunId, AttemptId, ProtocolVersion, Actor, Recipient, Type, InReplyToMessageId,
                        Summary, StructuredContentJson, Provenance, OccurredAtUtc)
                   VALUES
                       ({messageId}, {runId}, {attemptId}, {CollaborationMessage.ProtocolVersionOne},
                        {"Claude"}, {"Codex"}, {nameof(CollaborationMessageType.Acceptance)}, NULL,
                        {"Historical review"}, {"{\"rationale\":\"Historical evidence\"}"},
                        {nameof(CollaborationMessageProvenance.ProviderObserved)}, {now})");
        }

        await using (var context = fixture.CreateContext())
        {
            await context.Database.MigrateAsync();
        }

        await using var reopened = fixture.CreateContext();
        var run = await reopened.Runs.SingleAsync(candidate => candidate.Id == runId);
        Assert.Equal(ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex), run.ActiveParticipant);

        var attemptBoundEvent = await reopened.Events.SingleAsync(candidate => candidate.Id == attemptBoundEventId);
        Assert.Equal(
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            attemptBoundEvent.Actor);

        var attemptlessEvent = await reopened.Events.SingleAsync(candidate => candidate.Id == attemptlessEventId);
        Assert.Equal(
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            attemptlessEvent.Actor);

        var orchestratorEvent = await reopened.Events.SingleAsync(candidate => candidate.Id == orchestratorEventId);
        Assert.Equal(ParticipantIdentity.ForOrchestrator(), orchestratorEvent.Actor);

        var message = await reopened.CollaborationMessages.SingleAsync(candidate => candidate.Id == messageId);
        Assert.Equal(
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            message.Actor);
        Assert.Equal(
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            message.Recipient);
    }

    /// <summary>
    /// The correction round's own required "migration round-trip/backfill evidence": proves the
    /// <c>ArtifactCorrectionRound1</c> migration's <c>ALTER COLUMN Truncated</c> (NOT NULL bool ->
    /// nullable bool) preserves every pre-existing row's already-known true/false value verbatim
    /// — it never resets existing data to null or to a fixed default just because the column
    /// itself became nullable. A brand-new row inserted only after the migration (the Agent
    /// interrupted-recovery path) is the only case ever allowed to be null.
    /// </summary>
    [Fact]
    public async Task Migrate_preserves_every_pre_existing_artifacts_truncated_value_truthfully_when_the_column_becomes_nullable()
    {
        var now = DateTimeOffset.UtcNow;
        var truncatedTrueArtifactId = Guid.NewGuid();
        var truncatedFalseArtifactId = Guid.NewGuid();
        Guid runId;
        Guid attemptId;

        await using (var context = fixture.CreateContext())
        {
            // Stops one migration short of AgentAttemptCorrectionRound1, so artifacts.Truncated is
            // still the original NOT NULL column exactly as a pre-existing installation's database
            // has it right before upgrading.
            await context.Database.MigrateAsync("AddCodexPlanningAttempts");

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\migration-truncated-test", now);
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            runId = Guid.NewGuid();
            await InsertHistoricalRunAsync(context, runId, project.Id, "Prove truncated backfill", now);
            attemptId = Guid.NewGuid();

            // Raw SQL, not Attempt.Claim(...) + context.Attempts.Add(...): at this schema
            // checkpoint the attempts table has neither AgentResponseContract nor
            // AgentInputCollaborationMessageId yet, but the currently compiled Attempt/
            // AttemptConfiguration model already reflects both new nullable columns, so EF's own
            // insert would name columns this historical schema does not have — the same
            // rationale as the HostCapabilitySnapshot backfill test below.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({attemptId}, {runId}, {1}, {nameof(AttemptKind.Simulated)}, {nameof(AttemptStatus.Running)}, {now}, {""})");

            // Raw SQL: at this schema checkpoint Truncated is NOT NULL, but the currently compiled
            // Artifact/ArtifactConfiguration model already reflects the *new* nullable shape, so
            // EF's own Add(...) can't be trusted to match this historical schema — the same
            // rationale as the HostCapabilitySnapshot backfill test above.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO artifacts
                       (Id, RunId, AttemptId, Purpose, MediaType, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome, Sensitivity, RetentionPolicy, CreatedAtUtc)
                   VALUES
                       ({truncatedTrueArtifactId}, {runId}, {attemptId}, {nameof(ArtifactPurpose.ProcessStandardOutput)}, {"text/plain; charset=utf-8"}, {"runs/r/attempts/a/stdout.sealed"}, {"sha256:aaa"}, {128L}, {true}, {nameof(ArtifactCaptureOutcome.Captured)}, {nameof(ArtifactSensitivity.RedactedBestEffort)}, {nameof(ArtifactRetentionPolicy.RetainUntilRunDeleted)}, {now})");

            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO artifacts
                       (Id, RunId, AttemptId, Purpose, MediaType, RelativeStoragePath, ContentHash, ByteLength, Truncated, CaptureOutcome, Sensitivity, RetentionPolicy, CreatedAtUtc)
                   VALUES
                       ({truncatedFalseArtifactId}, {runId}, {attemptId}, {nameof(ArtifactPurpose.ProcessStandardError)}, {"text/plain; charset=utf-8"}, {"runs/r/attempts/a/stderr.sealed"}, {"sha256:bbb"}, {64L}, {false}, {nameof(ArtifactCaptureOutcome.Captured)}, {nameof(ArtifactSensitivity.RedactedBestEffort)}, {nameof(ArtifactRetentionPolicy.RetainUntilRunDeleted)}, {now})");
        }

        await using (var context = fixture.CreateContext())
        {
            // Brings the schema fully up to date, including AgentAttemptCorrectionRound1's
            // ALTER COLUMN making Truncated nullable.
            await context.Database.MigrateAsync();
        }

        await using var reopenedContext = fixture.CreateContext();

        var truncatedTrueRow = await reopenedContext.Artifacts.FindAsync(truncatedTrueArtifactId);
        Assert.NotNull(truncatedTrueRow);
        Assert.Equal(true, truncatedTrueRow.Truncated);

        var truncatedFalseRow = await reopenedContext.Artifacts.FindAsync(truncatedFalseArtifactId);
        Assert.NotNull(truncatedFalseRow);
        Assert.Equal(false, truncatedFalseRow.Truncated);

        // The only case ever allowed to be null: a brand-new row inserted after the migration,
        // through the real Agent interrupted-recovery path's own Truncated: null convention.
        var recoveredArtifact = Artifact.Record(
            Guid.NewGuid(), runId, attemptId, ArtifactPurpose.AgentStandardOutput, "text/plain; charset=utf-8",
            "runs/r/attempts/a/recovered-stdout.sealed", "sha256:ccc", 32, truncated: null,
            ArtifactCaptureOutcome.PartialHostInterrupted, ArtifactSensitivity.RedactedBestEffort,
            ArtifactRetentionPolicy.RetainUntilRunDeleted, now);
        reopenedContext.Artifacts.Add(recoveredArtifact);
        await reopenedContext.SaveChangesAsync();

        await using var finalContext = fixture.CreateContext();
        var recoveredRow = await finalContext.Artifacts.FindAsync(recoveredArtifact.Id);
        Assert.NotNull(recoveredRow);
        Assert.Null(recoveredRow.Truncated);
    }

    [Fact]
    public async Task Migrate_backfills_launch_kind_truthfully_for_every_pre_existing_snapshot_shape()
    {
        var now = DateTimeOffset.UtcNow;

        await using (var context = fixture.CreateContext())
        {
            // Stops one migration short of AddProviderLaunchTargets, so the table genuinely has
            // no LaunchKind/ResolvedScriptPath columns yet — exactly the shape a pre-existing
            // installation's database has right before upgrading.
            await context.Database.MigrateAsync("AddCollaborationMessages");

            // (a) An old successful direct-executable snapshot: before this migration, the only
            // way ResolvedExecutablePath could ever be set was through the direct-executable
            // path — this row must become DirectExecutable, not stay null.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO host_capability_snapshots
                       (Capability, ReasonCode, ResolvedExecutablePath, ObservedVersion, EvidenceObservedAtUtc, NextProbeDueAtUtc, ProbeDispatchedAtUtc)
                   VALUES
                       ({nameof(Capability.CodexCli)}, {nameof(CapabilityProbeReason.None)}, {@"C:\Program Files\nodejs\node.exe"}, {"0.9.0"}, {now}, {now.AddMinutes(5)}, {(DateTimeOffset?)null})");

            // (b) A failed probe that still retains last-known-good executable evidence from an
            // earlier success — must also be backfilled as DirectExecutable, not left null just
            // because its current ReasonCode is not None.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO host_capability_snapshots
                       (Capability, ReasonCode, ResolvedExecutablePath, ObservedVersion, EvidenceObservedAtUtc, NextProbeDueAtUtc, ProbeDispatchedAtUtc)
                   VALUES
                       ({nameof(Capability.ClaudeCli)}, {nameof(CapabilityProbeReason.ProbeTimedOut)}, {@"C:\Program Files\ClaudeCliStub\claude.exe"}, {"0.5.0"}, {now}, {now.AddMinutes(5)}, {(DateTimeOffset?)null})");

            // (c) A row that has never had a successful probe at all — must remain without a
            // launch target after the backfill, not gain one just because the migration ran.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO host_capability_snapshots
                       (Capability, ReasonCode, ResolvedExecutablePath, ObservedVersion, EvidenceObservedAtUtc, NextProbeDueAtUtc, ProbeDispatchedAtUtc)
                   VALUES
                       ({nameof(Capability.GitHubCli)}, {nameof(CapabilityProbeReason.ExecutableNotFound)}, {(string?)null}, {(string?)null}, {(DateTimeOffset?)null}, {now.AddMinutes(5)}, {(DateTimeOffset?)null})");
        }

        await using (var context = fixture.CreateContext())
        {
            // Brings the schema fully up to date, including AddProviderLaunchTargets.
            await context.Database.MigrateAsync();
        }

        await using var reopenedContext = fixture.CreateContext();

        var successfulRow = await reopenedContext.HostCapabilitySnapshots.FindAsync(Capability.CodexCli);
        Assert.NotNull(successfulRow);
        Assert.Equal(CapabilityProbeReason.None, successfulRow.ReasonCode);
        Assert.Equal(CapabilityLaunchKind.DirectExecutable, successfulRow.LaunchKind);
        Assert.Equal(@"C:\Program Files\nodejs\node.exe", successfulRow.ResolvedExecutablePath);
        Assert.Equal("0.9.0", successfulRow.ObservedVersion);
        Assert.Equal(now, successfulRow.EvidenceObservedAtUtc);
        // Never fabricated: a pre-existing row can never have observed a script path.
        Assert.Null(successfulRow.ResolvedScriptPath);

        var failedButRetainingEvidenceRow = await reopenedContext.HostCapabilitySnapshots.FindAsync(Capability.ClaudeCli);
        Assert.NotNull(failedButRetainingEvidenceRow);
        Assert.Equal(CapabilityProbeReason.ProbeTimedOut, failedButRetainingEvidenceRow.ReasonCode);
        Assert.Equal(CapabilityLaunchKind.DirectExecutable, failedButRetainingEvidenceRow.LaunchKind);
        Assert.Equal(@"C:\Program Files\ClaudeCliStub\claude.exe", failedButRetainingEvidenceRow.ResolvedExecutablePath);
        Assert.Null(failedButRetainingEvidenceRow.ResolvedScriptPath);

        var neverSuccessfulRow = await reopenedContext.HostCapabilitySnapshots.FindAsync(Capability.GitHubCli);
        Assert.NotNull(neverSuccessfulRow);
        Assert.Null(neverSuccessfulRow.ResolvedExecutablePath);
        Assert.Null(neverSuccessfulRow.LaunchKind);
        Assert.Null(neverSuccessfulRow.ResolvedScriptPath);
    }

    [Fact]
    public async Task Migrate_backfills_agent_response_contract_to_proposal_for_every_pre_existing_agent_attempt_but_never_for_other_kinds()
    {
        var now = DateTimeOffset.UtcNow;
        var agentAttemptId = Guid.NewGuid();
        var simulatedAttemptId = Guid.NewGuid();
        Guid runId;

        await using (var context = fixture.CreateContext())
        {
            // Stops one migration short of AddClaudeCriticalReviewAttempts, so the table
            // genuinely has no AgentResponseContract/AgentInputCollaborationMessageId columns
            // yet — exactly the shape a pre-existing installation's database has right before
            // upgrading.
            await context.Database.MigrateAsync("AgentAttemptCorrectionRound1");

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\migration-response-contract-test", now);
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            runId = Guid.NewGuid();
            await InsertHistoricalRunAsync(context, runId, project.Id, "Prove response contract backfill", now);

            // (a) A pre-existing Codex planning Agent attempt — before this migration, the only
            // response contract any Agent attempt could ever have was Proposal, so this row must
            // be truthfully backfilled to it, never left null.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({agentAttemptId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {now}, {""})");

            // (b) A pre-existing Simulated attempt — never touched by this migration's backfill,
            // which is scoped to Kind = 'Agent' only.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments)
                   VALUES ({simulatedAttemptId}, {runId}, {2}, {nameof(AttemptKind.Simulated)}, {nameof(AttemptStatus.Completed)}, {now}, {""})");
        }

        await using (var context = fixture.CreateContext())
        {
            // Brings the schema fully up to date, including AddClaudeCriticalReviewAttempts.
            await context.Database.MigrateAsync();
        }

        await using var reopenedContext = fixture.CreateContext();

        var agentRow = await reopenedContext.Attempts.FindAsync(agentAttemptId);
        Assert.NotNull(agentRow);
        Assert.Equal(AgentResponseContract.Proposal, agentRow.AgentResponseContract);
        // Never fabricated: a pre-existing Codex planning attempt never recorded an input
        // message (that concept did not exist until Claude critical review), so no
        // attempt_input_messages row is ever backfilled for it.
        Assert.False(await reopenedContext.AttemptInputMessages.AnyAsync(m => m.AttemptId == agentAttemptId));

        var simulatedRow = await reopenedContext.Attempts.FindAsync(simulatedAttemptId);
        Assert.NotNull(simulatedRow);
        Assert.Null(simulatedRow.AgentResponseContract);
    }

    [Fact]
    public async Task Migrate_backfills_attempt_input_messages_truthfully_from_the_retired_column_then_drops_it()
    {
        var now = DateTimeOffset.UtcNow;
        var criticalReviewAttemptId = Guid.NewGuid();
        var planningAttemptId = Guid.NewGuid();
        var reviewedProposalId = Guid.NewGuid();
        Guid runId;

        await using (var context = fixture.CreateContext())
        {
            // Stops one migration short of AddAttemptInputMessages, so the table genuinely has
            // the retired AgentInputCollaborationMessageId column and no attempt_input_messages
            // table yet — exactly the shape a pre-existing installation's database has right
            // before upgrading.
            await context.Database.MigrateAsync("AddClaudeCriticalReviewAttempts");

            var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\migration-input-messages-test", now);
            context.Projects.Add(project);
            await context.SaveChangesAsync();

            runId = Guid.NewGuid();
            await InsertHistoricalRunAsync(context, runId, project.Id, "Prove attempt input message backfill", now);

            // (a) A pre-existing critical-review attempt with a real reviewed Proposal — must be
            // truthfully backfilled as this attempt's sole input, at Sequence 0.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentInputCollaborationMessageId)
                   VALUES ({criticalReviewAttemptId}, {runId}, {1}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {now}, {""}, {reviewedProposalId})");

            // (b) A pre-existing Codex planning attempt, which never had an input message at
            // all — must gain no attempt_input_messages row just because the migration ran.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO attempts (Id, RunId, AttemptNumber, Kind, Status, ClaimedAtUtc, ProcessArguments, AgentInputCollaborationMessageId)
                   VALUES ({planningAttemptId}, {runId}, {2}, {nameof(AttemptKind.Agent)}, {nameof(AttemptStatus.Completed)}, {now}, {""}, {(Guid?)null})");
        }

        await using (var context = fixture.CreateContext())
        {
            // Brings the schema fully up to date, including AddAttemptInputMessages.
            await context.Database.MigrateAsync();
        }

        await using var reopenedContext = fixture.CreateContext();

        var backfilledInputMessage = await reopenedContext.AttemptInputMessages.SingleAsync(m => m.AttemptId == criticalReviewAttemptId);
        Assert.Equal(reviewedProposalId, backfilledInputMessage.CollaborationMessageId);
        Assert.Equal(0, backfilledInputMessage.Sequence);

        Assert.False(await reopenedContext.AttemptInputMessages.AnyAsync(m => m.AttemptId == planningAttemptId));

        // The retired column is genuinely gone — a raw read proves it, since the compiled
        // Attempt/AttemptConfiguration model no longer maps it at all and so could never prove
        // its absence on its own.
        var remainingColumnNames = await reopenedContext.Database
            .SqlQuery<string>($"SELECT name AS \"Value\" FROM pragma_table_info('attempts')")
            .ToListAsync();
        Assert.DoesNotContain("AgentInputCollaborationMessageId", remainingColumnNames);
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_claude_critical_review_attempt_and_its_resulting_acceptance()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\critical-review-migration-test", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Prove the critical-review schema", now);
        context.AddRange(project, run);
        await context.SaveChangesAsync();

        var proposal = CollaborationMessage.Record(
            Guid.NewGuid(),
            run.Id,
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal,
            null,
            "Record a bounded proposal.",
            "{\"scope\":\"Schema\",\"implementationSteps\":\"Add the migration\",\"risks\":\"Schema drift\",\"verificationPlan\":\"Migration test\",\"escalationPoints\":\"None expected\"}",
            CollaborationMessageProvenance.ProviderObserved,
            now);
        context.CollaborationMessages.Add(proposal);
        await context.SaveChangesAsync();

        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(),
            run.Id,
            attemptNumber: 2,
            gitWorkspaceId: Guid.NewGuid(),
            gitCheckpointId: Guid.NewGuid(),
            checkpointFingerprintSha256: "sha256:fingerprint",
            contextManifestArtifactId: Guid.NewGuid(),
            timeout: TimeSpan.FromMinutes(10),
            maxBytesPerStream: 1024,
            maxTotalCapturedBytes: 2048,
            claimedAtUtc: now);
        context.Attempts.Add(attempt);
        context.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, proposal.Id, sequence: 0));
        await context.SaveChangesAsync();

        attempt.MarkAgentDispatched(now);
        attempt.CompleteAgent(AgentOutcome.Accepted, "sha256:fingerprint", now);

        var acceptance = CollaborationMessage.Record(
            Guid.NewGuid(),
            run.Id,
            attempt.Id,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.CriticalReviewer, AgentProvider.ClaudeCode),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.Codex),
            CollaborationMessageType.Acceptance,
            proposal.Id,
            "The proposal is sound.",
            "{\"rationale\":\"The plan matches the objective and the diff evidence.\"}",
            CollaborationMessageProvenance.ProviderObserved,
            now);
        context.CollaborationMessages.Add(acceptance);
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var reopenedAttempt = await reopenedContext.Attempts.FindAsync(attempt.Id);
        Assert.NotNull(reopenedAttempt);
        Assert.Equal(AgentProvider.ClaudeCode, reopenedAttempt.AgentProvider);
        Assert.Equal(AgentRole.CriticalReviewer, reopenedAttempt.AgentRole);
        Assert.Equal(AgentResponseContract.CriticalReview, reopenedAttempt.AgentResponseContract);
        var reopenedInputMessage = await reopenedContext.AttemptInputMessages.SingleAsync(m => m.AttemptId == attempt.Id);
        Assert.Equal(proposal.Id, reopenedInputMessage.CollaborationMessageId);
        Assert.Equal(0, reopenedInputMessage.Sequence);
        Assert.Equal(AgentOutcome.Accepted, reopenedAttempt.AgentOutcome);
        Assert.Equal(AttemptStatus.Completed, reopenedAttempt.Status);

        var reopenedAcceptance = await reopenedContext.CollaborationMessages.SingleOrDefaultAsync(message => message.Id == acceptance.Id);
        Assert.NotNull(reopenedAcceptance);
        Assert.Equal(CollaborationMessageType.Acceptance, reopenedAcceptance.Type);
        Assert.Equal(proposal.Id, reopenedAcceptance.InReplyToMessageId);
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_bounded_collaboration_message()
    {
        var now = DateTimeOffset.UtcNow;
        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\ledger-migration-test", now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Prove the ledger schema", now);
        context.AddRange(project, run);
        context.CollaborationMessages.Add(CollaborationMessage.Record(
            Guid.NewGuid(),
            run.Id,
            null,
            CollaborationMessage.ProtocolVersionOne,
            ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
            ParticipantIdentity.ForAgentWithUnknownRole(AgentProvider.ClaudeCode),
            CollaborationMessageType.Proposal,
            null,
            "Record a bounded proposal.",
            "{\"scope\":\"Schema\",\"implementationSteps\":\"Add the migration\",\"risks\":\"Schema drift\",\"verificationPlan\":\"Migration test\",\"escalationPoints\":\"None expected\"}",
            CollaborationMessageProvenance.Simulated,
            now));

        await context.SaveChangesAsync();

        Assert.True(await context.CollaborationMessages.AnyAsync());
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_one_host_capability_snapshot_per_capability()
    {
        var now = DateTimeOffset.UtcNow;

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var snapshot = HostCapabilitySnapshot.Seed(Capability.Git, now);
        snapshot.MarkDispatched(now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\Program Files\Git\cmd\git.exe", null, "2.43.0", now, now.AddMinutes(5));
        context.HostCapabilitySnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persisted = await reopenedContext.HostCapabilitySnapshots.FindAsync(Capability.Git);

        Assert.NotNull(persisted);
        Assert.Equal(CapabilityProbeReason.None, persisted.ReasonCode);
        Assert.Equal("2.43.0", persisted.ObservedVersion);
        Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", persisted.ResolvedExecutablePath);
        Assert.Null(persisted.ProbeDispatchedAtUtc);

        // Capability is the natural primary key: inserting a second row for the same
        // capability must be rejected, not silently duplicated.
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            reopenedContext.HostCapabilitySnapshots.Add(HostCapabilitySnapshot.Seed(Capability.Git, now));
            await reopenedContext.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Migrate_creates_a_schema_that_accepts_a_process_attempt_with_its_durable_intent_and_defaults_prior_rows_to_simulated()
    {
        var now = DateTimeOffset.UtcNow;

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\process-migration-test", DateTimeOffset.UtcNow);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var simulatedRun = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Simulated", now);
        context.Runs.Add(simulatedRun);
        await context.SaveChangesAsync();
        simulatedRun.Claim(now);
        var simulatedAttempt = Attempt.Claim(Guid.NewGuid(), simulatedRun.Id, attemptNumber: 1, now);
        context.Attempts.Add(simulatedAttempt);
        await context.SaveChangesAsync();

        var processRun = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Process", now);
        context.Runs.Add(processRun);
        await context.SaveChangesAsync();
        processRun.Claim(now);
        var intent = new ProcessExecutionIntent(
            ExecutablePath: @"C:\tools\build.exe",
            Arguments: ["--verify"],
            WorkingDirectory: @"C:\repos\devalcopilot",
            ApprovedRoot: @"C:\repos",
            Timeout: TimeSpan.FromMinutes(5),
            MaxBytesPerStream: 65536,
            MaxTotalCapturedBytes: 131072);
        var processAttempt = Attempt.ClaimProcess(Guid.NewGuid(), processRun.Id, attemptNumber: 1, intent, now);
        context.Attempts.Add(processAttempt);
        await context.SaveChangesAsync();
        processAttempt.MarkProcessDispatched(now);
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persistedSimulated = await reopenedContext.Attempts.FindAsync(simulatedAttempt.Id);
        var persistedProcess = await reopenedContext.Attempts.FindAsync(processAttempt.Id);

        Assert.NotNull(persistedSimulated);
        Assert.Equal(AttemptKind.Simulated, persistedSimulated.Kind);
        Assert.Null(persistedSimulated.ProcessDispatchedAtUtc);

        Assert.NotNull(persistedProcess);
        Assert.Equal(AttemptKind.Process, persistedProcess.Kind);
        Assert.Equal(intent.ExecutablePath, persistedProcess.ProcessExecutablePath);
        Assert.Equal(intent.Arguments, persistedProcess.ProcessArguments);
        Assert.Equal(intent.Timeout, persistedProcess.ProcessTimeout);
        Assert.Equal(now, persistedProcess.ProcessDispatchedAtUtc);
    }

    [Fact]
    public async Task Mutating_the_caller_owned_argument_list_after_ClaimProcess_does_not_change_what_is_persisted()
    {
        var now = DateTimeOffset.UtcNow;
        var mutableArguments = new List<string> { "--verify" };

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\argument-immutability-test", DateTimeOffset.UtcNow);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Process", now);
        context.Runs.Add(run);
        await context.SaveChangesAsync();
        run.Claim(now);

        var intent = new ProcessExecutionIntent(
            ExecutablePath: @"C:\tools\build.exe",
            Arguments: mutableArguments,
            WorkingDirectory: @"C:\repos\devalcopilot",
            ApprovedRoot: @"C:\repos",
            Timeout: TimeSpan.FromMinutes(5),
            MaxBytesPerStream: 65536,
            MaxTotalCapturedBytes: 131072);
        var attempt = Attempt.ClaimProcess(Guid.NewGuid(), run.Id, attemptNumber: 1, intent, now);
        context.Attempts.Add(attempt);

        // Mutated after ClaimProcess but before SaveChanges — a lazily-evaluated conversion
        // reading the caller's own list at save time would otherwise leak this tampering into
        // what gets persisted.
        mutableArguments.Add("--tampered");
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persistedAttempt = await reopenedContext.Attempts.FindAsync(attempt.Id);

        Assert.NotNull(persistedAttempt);
        Assert.Equal(["--verify"], persistedAttempt.ProcessArguments);
    }

    [Fact]
    public async Task Migrate_persists_project_verification_commands_with_monotonic_numbers_and_literal_arguments()
    {
        var now = DateTimeOffset.UtcNow;
        var arguments = new List<string> { "test", "--no-restore" };

        await using var context = fixture.CreateContext();
        await context.Database.MigrateAsync();

        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", @"C:\repos\verification-migration-test", now);
        context.Projects.Add(project);
        await context.SaveChangesAsync();

        var verificationCommand = VerificationCommand.Configure(
            Guid.NewGuid(),
            project.Id,
            project.ReserveVerificationCommandNumber(),
            "Backend tests",
            @"C:\Program Files\dotnet\dotnet.exe",
            arguments,
            300,
            true,
            now);
        context.VerificationCommands.Add(verificationCommand);
        arguments.Add("--tampered");
        await context.SaveChangesAsync();

        await using var reopenedContext = fixture.CreateContext();
        var persisted = await reopenedContext.VerificationCommands.SingleAsync(command => command.ProjectId == project.Id);

        Assert.Equal(1, persisted.CommandNumber);
        Assert.Equal(["test", "--no-restore"], persisted.Arguments);
        Assert.Equal(2, (await reopenedContext.Projects.SingleAsync(savedProject => savedProject.Id == project.Id)).NextVerificationCommandNumber);
    }

    private static Task<int> InsertHistoricalRunAsync(
        DevalCopilotDbContext context,
        Guid runId,
        Guid projectId,
        string objective,
        DateTimeOffset nowUtc) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO runs
                   (Id, ProjectId, ExecutionNumber, Objective, Lifecycle, Stage, ActiveParticipant,
                    CreatedAtUtc, LastAdvancedAtUtc, AccumulatedAutonomousSeconds)
               VALUES
                   ({runId}, {projectId}, {1}, {objective}, {nameof(RunLifecycle.Created)},
                    {nameof(RunStage.Intake)}, {nameof(ParticipantKind.None)}, {nowUtc}, {nowUtc}, {0d})");
}
