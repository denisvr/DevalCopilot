using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class RecordImplementationResultCommandHandlerTests(SqliteDatabaseFixture fixture) : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly string StartingHeadSha = new('a', 40);
    private static readonly string StartingFingerprint = new('a', 64);
    private static readonly string ChangedFingerprint = new('b', 64);
    private static readonly string ChangedHeadSha = new('c', 40);
    private static readonly IReadOnlyList<SealedImplementationArtifact> NoArtifacts = [];

    private static ValidatedImplementationReport Report(IReadOnlyList<string> changedRelativePaths) =>
        ValidatedImplementationReport.Create(
            "Implemented the ledger table and its query.",
            changedRelativePaths,
            "Added the migration and the query handler.",
            string.Empty,
            string.Empty,
            "Run the backend test suite.");

    private static (Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint StartingCheckpoint, Attempt Attempt, AttemptInputMessage Proposal)
        CreateClaimedImplementationAttempt()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Implement the resolved plan", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", StartingHeadSha, "main", Now);
        workspace.MarkReady();

        var startingCheckpoint = GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), Now, StartingHeadSha, StartingFingerprint, []);

        var attempt = Attempt.ClaimAgentImplementation(
            Guid.NewGuid(), run.Id, 1, workspace.Id, startingCheckpoint.Id, StartingFingerprint,
            Guid.NewGuid(), TimeSpan.FromMinutes(20), 262144, 524288, Now);
        var proposal = AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), sequence: 0);

        return (project, run, workspace, startingCheckpoint, attempt, proposal);
    }

    private async Task<(Project Project, Run Run, GitWorkspace Workspace, Attempt Attempt)> SeedAndDispatchAsync(DevalCopilotDbContext dbContext)
    {
        var (project, run, workspace, startingCheckpoint, attempt, proposal) = CreateClaimedImplementationAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(startingCheckpoint);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(proposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return (project, run, workspace, attempt);
    }

    [Fact]
    public async Task HandleAsync_records_Implemented_when_reported_paths_exactly_match_observed_git_evidence()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts,
                Report(["src/Foo.cs"]), null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.Implemented, result.Value.Outcome);
        Assert.Equal(AttemptStatus.Completed, attempt.Status);
        Assert.NotNull(attempt.AgentResultGitCheckpointId);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);

        var newCheckpoint = Assert.Single(dbContext.GitCheckpoints, c => c.Id == attempt.AgentResultGitCheckpointId);
        Assert.Equal(ChangedFingerprint, newCheckpoint.FingerprintSha256);
        Assert.Equal(StartingHeadSha, newCheckpoint.HeadCommitSha);
        var changedFile = Assert.Single(dbContext.GitChangedFiles, f => f.CheckpointId == newCheckpoint.Id);
        Assert.Equal("src/Foo.cs", changedFile.Path);

        var executionReport = Assert.Single(
            dbContext.CollaborationMessages, m => m.AttemptId == attempt.Id && m.Type == CollaborationMessageType.ExecutionReport);
        Assert.Equal(ParticipantKind.Claude, executionReport.Actor);
    }

    [Fact]
    public async Task HandleAsync_records_InvalidStructuredOutput_and_flags_needs_attention_when_reported_paths_do_not_match_git_evidence()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Bar.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts,
                Report(["src/Foo.cs"]), null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, result.Value.Outcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_records_NoChangesProduced_when_the_report_is_valid_but_the_fingerprint_is_unchanged()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, StartingFingerprint, [], NoArtifacts, Report([]), null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.NoChangesProduced, result.Value.Outcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_records_ProviderInvocationFailed_and_leaves_workspace_ready_when_nothing_changed()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, false, StartingHeadSha, StartingFingerprint, [], NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, result.Value.Outcome);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_flags_needs_attention_when_the_process_failed_but_the_worktree_was_mutated_anyway()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        // A changed fingerprint must be paired with at least one observed changed path to be
        // coherent — an empty path list would contradict the fingerprint having moved at all.
        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ") };
        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, false, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, result.Value.Outcome);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_records_CheckpointEvidenceUnavailable_and_flags_needs_attention_when_evidence_capture_failed()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(run.Id, attempt.Id, true, null, null, [], NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.CheckpointEvidenceUnavailable, result.Value.Outcome);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_report_supplied_for_a_failed_invocation()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, _, attempt) = await SeedAndDispatchAsync(dbContext);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, false, StartingHeadSha, StartingFingerprint, [], NoArtifacts, Report([]), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.conflicting_implementation_evidence", Assert.Single(result.Errors).Code);
    }

    // --- Correction-round hardening: boundary-value validation, zero mutation on malformed input ---

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_when_the_starting_checkpoint_no_longer_exists()
    {
        await using var dbContext = fixture.CreateContext();
        var (proj, r, workspace, _, att, prop) = CreateClaimedImplementationAttempt();
        att.MarkAgentDispatched(Now);
        dbContext.Projects.Add(proj);
        dbContext.Runs.Add(r);
        dbContext.GitWorkspaces.Add(workspace);
        // Deliberately never add the starting checkpoint row, simulating it having been lost or
        // never persisted — the attempt's own reference to it must never be trusted blindly.
        dbContext.Attempts.Add(att);
        dbContext.AttemptInputMessages.Add(prop);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                r.Id, att.Id, true, StartingHeadSha, ChangedFingerprint, [], NoArtifacts, Report([]), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.starting_checkpoint_invalid", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, att.Status);
        Assert.Empty(dbContext.GitCheckpoints.Where(c => c.WorkspaceId == workspace.Id));
    }

    [Theory]
    [InlineData("not-a-sha", null)]
    [InlineData(null, "not-a-fingerprint")]
    public async Task HandleAsync_fails_with_zero_mutation_for_a_malformed_completion_head_or_fingerprint_shape(
        string? malformedHead, string? malformedFingerprint)
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true,
                malformedHead ?? StartingHeadSha,
                malformedFingerprint ?? StartingFingerprint,
                [], NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        var errorCode = Assert.Single(result.Errors).Code;
        Assert.True(
            errorCode is "agent_attempts.invalid_completion_head" or "agent_attempts.invalid_completion_fingerprint",
            $"Unexpected error code: {errorCode}");
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_for_duplicate_observed_changed_paths()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var observedPaths = new[]
        {
            new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " "),
            new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " "),
        };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts, Report(["src/Foo.cs"]), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_observed_changed_paths", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_for_an_unsafe_observed_changed_path()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var observedPaths = new[] { new GitWorkspaceChangedPath("../outside.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts, Report(["../outside.cs"]), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_observed_changed_paths", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Theory]
    [InlineData("X", " ")]
    [InlineData(" ", "9")]
    [InlineData("", " ")]
    public async Task HandleAsync_fails_with_zero_mutation_for_an_undefined_git_status_value(string indexStatus, string workTreeStatus)
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, indexStatus, workTreeStatus) };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts, Report(["src/Foo.cs"]), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_observed_changed_paths", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_for_a_hand_built_report_with_an_unsafe_changed_path()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        // Never produced by ImplementationResponseParser (which already rejects this shape) —
        // constructed directly via the report's public factory, exactly as a caller that skipped
        // the parser could. The observed evidence is itself safe and unrelated, so this test
        // proves the report's own boundary re-validation is what rejects it — not a coincidental
        // rejection of the (also unsafe) observed path.
        var unsafeReport = Report(["../outside.cs"]);
        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts, unsafeReport, null),
            CancellationToken.None);

        // A supplied-but-invalid report is a malformed command-boundary object, never silently
        // downgraded to a truthful provider outcome like InvalidStructuredOutput — it fails this
        // command closed, with zero mutation to Attempt, Workspace, or the ledger.
        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_implementation_report", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
        // Only the pre-existing starting checkpoint — no new result checkpoint was ever created.
        Assert.Single(dbContext.GitCheckpoints.Where(c => c.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_for_a_hand_built_report_with_a_duplicated_path()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        // A duplicated path is safe on its own (both entries are valid repository-relative
        // paths), so it passes observed-path validation but must still fail the report's own
        // independent re-validation — proving the handler never relies solely on
        // ImplementationResponseParser having already rejected it.
        var duplicatePathReport = Report(["src/Foo.cs", "src/Foo.cs"]);
        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, observedPaths, NoArtifacts, duplicatePathReport, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_implementation_report", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Null(attempt.AgentOutcome);
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task HandleAsync_records_ImplementationHeadChanged_and_flags_needs_attention_even_when_reported_paths_match_evidence()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        // Paths match exactly, and the process reported success — the one remaining signal that
        // this was never a trustworthy Claude-only implementation is that HEAD itself moved,
        // which Claude's tool allowlist can never do on its own.
        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, ChangedHeadSha, ChangedFingerprint, observedPaths, NoArtifacts,
                Report(["src/Foo.cs"]), null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentOutcome.ImplementationHeadChanged, result.Value.Outcome);
        Assert.Equal(AttemptStatus.Failed, attempt.Status);
        Assert.Null(attempt.AgentResultGitCheckpointId);
        Assert.Equal(WorkspaceStatus.NeedsAttention, workspace.Status);
        Assert.Empty(dbContext.CollaborationMessages.Where(m => m.AttemptId == attempt.Id));
    }

    // --- Correction round: starting-checkpoint consistency and completion-evidence coherence ---

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_when_the_starting_checkpoints_fingerprint_disagrees_with_the_attempts_own_recorded_fingerprint()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, workspace, startingCheckpoint, attempt, proposal) = CreateClaimedImplementationAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        // The persisted starting checkpoint's own fingerprint is corrupted/inconsistent relative
        // to what the attempt itself recorded as its starting fingerprint at claim time — this
        // must never be silently trusted as "the same checkpoint".
        var inconsistentCheckpoint = GitCheckpoint.Capture(
            startingCheckpoint.Id, workspace.Id, workspace.ReserveCheckpointNumber(), Now, StartingHeadSha, ChangedFingerprint, []);
        dbContext.GitCheckpoints.Add(inconsistentCheckpoint);
        dbContext.Attempts.Add(attempt);
        dbContext.AttemptInputMessages.Add(proposal);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, ChangedFingerprint, [], NoArtifacts, Report([]), null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.starting_checkpoint_invalid", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HandleAsync_fails_with_zero_mutation_for_partial_completion_evidence(bool headPresent, bool fingerprintPresent)
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true,
                headPresent ? StartingHeadSha : null,
                fingerprintPresent ? StartingFingerprint : null,
                [], NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.incoherent_completion_evidence", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_when_unavailable_evidence_carries_observed_changed_paths()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(run.Id, attempt.Id, true, null, null, observedPaths, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.incoherent_completion_evidence", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }

    [Fact]
    public async Task HandleAsync_fails_with_zero_mutation_when_an_unchanged_fingerprint_carries_observed_changed_paths()
    {
        await using var dbContext = fixture.CreateContext();
        var (_, run, workspace, attempt) = await SeedAndDispatchAsync(dbContext);

        var observedPaths = new[] { new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ") };
        var handler = new RecordImplementationResultCommandHandler(dbContext, new FixedTimeProvider(Now.AddMinutes(1)));

        var result = await handler.HandleAsync(
            new RecordImplementationResultCommand(
                run.Id, attempt.Id, true, StartingHeadSha, StartingFingerprint, observedPaths, NoArtifacts, null, null),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.incoherent_completion_evidence", Assert.Single(result.Errors).Code);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(WorkspaceStatus.Ready, workspace.Status);
    }
}
