using System.Security.Cryptography;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class CreateCodexPlanningAttemptCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeGitWorkspaceEvidenceReader(GitWorkspaceEvidenceResult result) : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public static FakeGitWorkspaceEvidenceReader MatchingCheckpoint(string fingerprintSha256) => new(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), fingerprintSha256, [], null));
    }

    /// <summary>
    /// Deterministically simulates a concurrent request winning the race for one run's single
    /// Running-attempt slot: the injected action runs exactly once, at the exact point the real
    /// handler calls <see cref="IGitWorkspaceEvidenceReader.CaptureAsync"/> — strictly after the
    /// handler's own in-process eligibility pre-check has already passed (nothing was Running
    /// yet) and strictly before its own final <c>SaveChangesAsync</c>. No real threading, no
    /// timing-dependent flakiness: the race is reproduced by construction, every run.
    /// </summary>
    private sealed class RaceInjectingEvidenceReader(GitWorkspaceEvidenceResult result, Func<CancellationToken, Task> injectRace)
        : IGitWorkspaceEvidenceReader
    {
        private bool _injected;

        public async Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken)
        {
            if (!_injected)
            {
                _injected = true;
                await injectRace(cancellationToken);
            }

            return result;
        }
    }

    /// <summary>A minimal, deterministic in-memory fake — no real filesystem or hashing
    /// dependency beyond what a unit test can trivially verify against.</summary>
    private sealed class FakeArtifactStore : IArtifactStore
    {
        private readonly Dictionary<string, byte[]> _partialContent = new(StringComparer.Ordinal);

        private static readonly string PartialRoot = Path.Combine(Path.GetTempPath(), "devalcopilot-app-tests-partials");

        public bool SealShouldFail { get; set; }

        // The handler performs real Directory.CreateDirectory/File.WriteAllTextAsync calls
        // directly against whatever this returns (it does not go through IArtifactStore for the
        // write itself) — so, unlike every other member here, this must be a real, valid
        // filesystem path, not an opaque in-memory key.
        public string GetPartialPath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            Path.Combine(PartialRoot, $"{runId:N}", $"{attemptId:N}", $"{purpose}.partial");

        public string GetSealedRelativePath(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            $"{runId:N}/{attemptId:N}/{purpose}.sealed";

        public Task<SealedOutputFile?> SealAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken)
        {
            if (SealShouldFail)
            {
                return Task.FromResult<SealedOutputFile?>(null);
            }

            var partialPath = GetPartialPath(runId, attemptId, purpose);
            var bytes = _partialContent.TryGetValue(partialPath, out var written) ? written : [];
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            return Task.FromResult<SealedOutputFile?>(
                new SealedOutputFile(GetSealedRelativePath(runId, attemptId, purpose), bytes.LongLength, hash));
        }

        public bool HasSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

        public bool HasPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) => false;

        public Task<SealedOutputFile?> DescribeSealedFileAsync(Guid runId, Guid attemptId, ArtifactPurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult<SealedOutputFile?>(null);

        public void DeleteOrphanedPartialFile(Guid runId, Guid attemptId, ArtifactPurpose purpose)
        {
        }

        public HashSet<(Guid RunId, Guid AttemptId, ArtifactPurpose Purpose)> DeletedSealedFiles { get; } = new();

        public void DeleteOrphanedSealedFile(Guid runId, Guid attemptId, ArtifactPurpose purpose) =>
            DeletedSealedFiles.Add((runId, attemptId, purpose));

        public Task<PartialReadWindow> ReadPartialAsync(
            Guid runId, Guid attemptId, ArtifactPurpose purpose, long fromOffset, int maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult(new PartialReadWindow(string.Empty, fromOffset, 0));

        public Task<SealedReadWindow> VerifyAndReadSealedAsync(
            string relativeStoragePath, long expectedByteLength, string expectedContentHash, long fromOffset, int maxBytes,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SealedReadWindow(SealedReadStatus.Missing, string.Empty, fromOffset, 0));

        /// <summary>Records what the handler actually wrote to the partial path, mirroring the
        /// real store's contract of writing before sealing — the handler itself uses
        /// <c>File.WriteAllTextAsync</c> against the returned path, so this fake intercepts
        /// nothing; instead the test seeds the expected bytes directly for hashing purposes when
        /// asserting on the sealed artifact.</summary>
        public void SeedPartialContentForHashing(Guid runId, Guid attemptId, ArtifactPurpose purpose, byte[] content) =>
            _partialContent[GetPartialPath(runId, attemptId, purpose)] = content;
    }

    private async Task<(Project Project, Run Run, GitWorkspace Workspace, GitCheckpoint Checkpoint)> SeedEligibleRunAsync(
        DevalCopilotDbContext dbContext,
        bool claimRun = false,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool hasCheckpoint = true,
        bool codexObserved = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        if (claimRun)
        {
            run.Claim(Now);
        }

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        if (workspaceReady)
        {
            workspace.MarkReady();
        }

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);

        if (hasCheckpoint)
        {
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
            dbContext.GitCheckpoints.Add(checkpoint);
        }

        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now);
        if (!leaseActive)
        {
            lease.Release(Now);
        }

        dbContext.RepositoryMutationLeases.Add(lease);

        var codex = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        if (codexObserved)
        {
            codex.MarkDispatched(Now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        }

        dbContext.HostCapabilitySnapshots.Add(codex);

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var checkpointEntity = hasCheckpoint
            ? await dbContext.GitCheckpoints.SingleAsync(c => c.WorkspaceId == workspace.Id)
            : null;

        return (project, run, workspace, checkpointEntity!);
    }

    [Fact]
    public async Task HandleAsync_creates_an_agent_attempt_and_seals_the_context_manifest_and_claims_a_created_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (project, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: false);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AttemptNumber);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);

        var attempt = Assert.Single(dbContext.Attempts, a => a.RunId == run.Id);
        Assert.Equal(AttemptKind.Agent, attempt.Kind);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
        Assert.Equal(workspace.Id, attempt.AgentGitWorkspaceId);
        Assert.Equal(checkpoint.Id, attempt.AgentGitCheckpointId);
        Assert.Equal(Fingerprint, attempt.AgentCheckpointFingerprintSha256);
        Assert.NotNull(attempt.AgentContextManifestArtifactId);

        var manifestArtifact = Assert.Single(
            dbContext.Artifacts, a => a.AttemptId == attempt.Id && a.Purpose == ArtifactPurpose.AgentContextManifest);
        Assert.Equal(attempt.AgentContextManifestArtifactId, manifestArtifact.Id);
        Assert.Equal("application/json", manifestArtifact.MediaType);
        Assert.Equal(ArtifactCaptureOutcome.Captured, manifestArtifact.CaptureOutcome);
        Assert.Equal(ArtifactSensitivity.HostConstructedContent, manifestArtifact.Sensitivity);
        Assert.Equal(ArtifactRetentionPolicy.RetainUntilRunDeleted, manifestArtifact.RetentionPolicy);
        Assert.False(manifestArtifact.Truncated);
    }

    [Fact]
    public async Task HandleAsync_does_not_reclaim_a_run_that_is_already_running()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claimRun: true);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RunLifecycle.Running, run.Lifecycle);
    }

    [Fact]
    public async Task HandleAsync_increments_the_attempt_number_from_prior_attempts_on_the_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var priorAttempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        priorAttempt.Complete(Now.AddSeconds(1));
        dbContext.Attempts.Add(priorAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.AttemptNumber);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_run_does_not_exist()
    {
        await using var dbContext = _fixture.CreateContext();

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_found", Assert.Single(result.Errors).Code);
    }

    // NOTE: originally authored as a [Theory] over a bare `bool completeRunFirst`, whose
    // `false` case left the run merely Claimed (Running) — an actively eligible state the
    // handler correctly accepts, not a terminal one — so that case asserted the wrong outcome
    // for what its name promised. Rewritten as a MemberData theory over every genuinely
    // terminal Run transition instead of removing coverage outright.
    [Theory]
    [MemberData(nameof(TerminalRunTransitions))]
    public async Task HandleAsync_fails_when_the_run_is_already_terminal(Action<Run, DateTimeOffset> moveToTerminalState)
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        moveToTerminalState(run, Now);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("runs.not_active", Assert.Single(result.Errors).Code);
    }

    public static TheoryData<Action<Run, DateTimeOffset>> TerminalRunTransitions() => new()
    {
        (run, now) => run.Complete(now),
        (run, now) => run.Fail(now),
        (run, now) => run.MarkInterrupted(now),
    };

    [Fact]
    public async Task HandleAsync_fails_when_no_git_workspace_exists_for_the_project()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No workspace yet", Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.workspace_not_ready", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_workspace_is_not_ready()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, workspaceReady: false);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.workspace_not_ready", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_no_lease_is_active_for_the_workspace()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, leaseActive: false);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.lease_not_active", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_no_checkpoint_exists_for_the_workspace()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, hasCheckpoint: false);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_missing", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_an_agent_attempt_is_already_running_for_the_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        dbContext.Attempts.Add(Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);
    }

    // The run-wide invariant: a Codex plan must never be creatable while ANY attempt kind — not
    // just a prior Agent attempt — is Running for this run.
    [Theory]
    [MemberData(nameof(NonAgentRunningAttemptFactories))]
    public async Task HandleAsync_fails_when_a_non_agent_attempt_is_already_running_for_the_run(Func<Guid, DateTimeOffset, Attempt> claim)
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        dbContext.Attempts.Add(claim(run.Id, Now));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);
        Assert.Single(dbContext.Attempts.Where(a => a.RunId == run.Id));
    }

    public static TheoryData<Func<Guid, DateTimeOffset, Attempt>> NonAgentRunningAttemptFactories() => new()
    {
        (runId, now) => Attempt.Claim(Guid.NewGuid(), runId, 1, now),
        (runId, now) => Attempt.ClaimProcess(
            Guid.NewGuid(), runId, 1,
            new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos", TimeSpan.FromMinutes(5), 65536, 131072),
            now),
    };

    // Reproduces the exact race the filtered database unique index exists to close: a
    // competing request commits its own Running attempt for this run strictly between this
    // handler's own pre-check and its own final SaveChangesAsync. The handler must convert the
    // resulting DbUpdateException into the same safe conflict the pre-check itself would have
    // reported, never let it escape as an unhandled exception, and never leave the manifest it
    // already sealed as a permanent orphan.
    [Fact]
    public async Task HandleAsync_resolves_a_lost_race_as_a_safe_conflict_with_no_orphaned_manifest()
    {
        await using var seedContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(seedContext, claimRun: true);
        var runId = run.Id;

        await using var raceContext = _fixture.CreateContext();
        await using var handlerContext = _fixture.CreateContext();

        var artifactStore = new FakeArtifactStore();
        var evidenceReader = new RaceInjectingEvidenceReader(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
            async cancellationToken =>
            {
                raceContext.Attempts.Add(Attempt.Claim(Guid.NewGuid(), runId, 1, Now));
                await raceContext.SaveChangesAsync(cancellationToken);
            });

        var handler = new CreateCodexPlanningAttemptCommandHandler(handlerContext, evidenceReader, artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.run_has_active_attempt", Assert.Single(result.Errors).Code);

        await using var verifyContext = _fixture.CreateContext();
        var runningAttempts = await verifyContext.Attempts.Where(a => a.RunId == runId && a.Status == AttemptStatus.Running).ToListAsync();
        var survivor = Assert.Single(runningAttempts);
        // The race winner — never the loser's Agent attempt, which must never have been committed.
        Assert.Equal(AttemptKind.Simulated, survivor.Kind);
        Assert.Empty(verifyContext.Attempts.Where(a => a.RunId == runId && a.Kind == AttemptKind.Agent));

        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
    }

    // Correction C: a DbUpdateException whose cause is NOT a competing Running attempt must
    // never be misreported as attempts.run_has_active_attempt — that would falsely claim a race
    // that never happened. Reproduces a DbUpdateException via a DIFFERENT unique index than the
    // filtered Running-attempt one the test above exercises: the plain (RunId, AttemptNumber)
    // index. Using the same RaceInjectingEvidenceReader technique (injected strictly between the
    // handler's own pre-check and its own SaveChangesAsync), a competing request commits a
    // TERMINAL attempt at the same AttemptNumber the handler is about to compute for itself. This
    // run has no attempts before the race, so the handler's own count-based numbering — evaluated
    // AFTER the race injection point, and therefore seeing the injected row — computes attemptNumber
    // = 1 (existing row) + 1 = 2; the injected row is seeded with AttemptNumber 2 to collide with
    // it. Because the injected attempt is immediately completed (never Running), the handler's own
    // post-catch "is there a competing Running attempt" check is false, forcing the
    // attempts.persistence_failed branch rather than the race-conflict branch.
    [Fact]
    public async Task HandleAsync_reports_persistence_failed_when_a_db_update_exception_is_not_caused_by_a_competing_running_attempt()
    {
        await using var seedContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(seedContext, claimRun: true);
        var runId = run.Id;

        await using var raceContext = _fixture.CreateContext();
        await using var handlerContext = _fixture.CreateContext();

        var artifactStore = new FakeArtifactStore();
        var evidenceReader = new RaceInjectingEvidenceReader(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null),
            async cancellationToken =>
            {
                // Not a competing Running attempt — a terminal one, at the exact AttemptNumber
                // the handler will independently compute and try to insert for itself, so the
                // handler's own SaveChangesAsync fails on the (RunId, AttemptNumber) unique index
                // rather than the filtered Running-attempt index.
                var competing = Attempt.Claim(Guid.NewGuid(), runId, 2, Now);
                competing.Complete(Now);
                raceContext.Attempts.Add(competing);
                await raceContext.SaveChangesAsync(cancellationToken);
            });

        var handler = new CreateCodexPlanningAttemptCommandHandler(handlerContext, evidenceReader, artifactStore, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(runId), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.persistence_failed", Assert.Single(result.Errors).Code);

        await using var verifyContext = _fixture.CreateContext();
        Assert.Empty(verifyContext.Attempts.Where(a => a.RunId == runId && a.Kind == AttemptKind.Agent));
        Assert.Single(artifactStore.DeletedSealedFiles, entry => entry.Purpose == ArtifactPurpose.AgentContextManifest);
    }

    [Fact]
    public async Task HandleAsync_succeeds_when_a_prior_agent_attempt_on_the_run_is_already_terminal()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, workspace, checkpoint) = await SeedEligibleRunAsync(dbContext, claimRun: true);
        var priorAttempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        priorAttempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Attempts.Add(priorAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.AttemptNumber);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_codex_capability_was_never_observed_successfully()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext, codexObserved: false);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_not_observed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_there_is_no_codex_capability_snapshot_at_all()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "No probe seeded", Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, new byte[16], Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint), new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.provider_not_observed", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_when_fresh_git_evidence_capture_is_not_successful()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var evidenceReader = new FakeGitWorkspaceEvidenceReader(
            new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.GitInvocationFailed, null, null, [], null));
        var handler = new CreateCodexPlanningAttemptCommandHandler(dbContext, evidenceReader, new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Attempts.Where(a => a.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_when_fresh_git_evidence_fingerprint_no_longer_matches_the_checkpoint()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var evidenceReader = FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(new string('b', 64));
        var handler = new CreateCodexPlanningAttemptCommandHandler(dbContext, evidenceReader, new FakeArtifactStore(), new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.checkpoint_not_current", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Attempts.Where(a => a.RunId == run.Id));
    }

    [Fact]
    public async Task HandleAsync_fails_and_creates_no_attempt_when_sealing_the_context_manifest_fails()
    {
        await using var dbContext = _fixture.CreateContext();
        var (_, run, _, _) = await SeedEligibleRunAsync(dbContext);

        var handler = new CreateCodexPlanningAttemptCommandHandler(
            dbContext, FakeGitWorkspaceEvidenceReader.MatchingCheckpoint(Fingerprint),
            new FakeArtifactStore { SealShouldFail = true }, new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.context_manifest_seal_failed", Assert.Single(result.Errors).Code);
        Assert.Empty(dbContext.Attempts.Where(a => a.RunId == run.Id));
    }
}
