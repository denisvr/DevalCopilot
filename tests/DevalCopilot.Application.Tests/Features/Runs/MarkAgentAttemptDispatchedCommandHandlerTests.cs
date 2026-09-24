using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class MarkAgentAttemptDispatchedCommandHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint = new('a', 64);

    private static (Project Project, Run Run, Attempt Attempt) CreateClaimedAgentAttempt()
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        return (project, run, attempt);
    }

    /// <summary>
    /// A fully eligible Agent attempt — Running run, Ready workspace, Active lease, current
    /// checkpoint — the exact shape <see cref="MarkAgentAttemptDispatchedCommandHandler"/>'s own
    /// last-gate revalidation requires. Each knob independently breaks exactly one of those facts
    /// so a test can prove that specific gate, and nothing else, causes the rejection.
    /// </summary>
    private static async Task<(Run Run, Attempt Attempt)> SeedEligibleAttemptAsync(
        DevalCopilotDbContext dbContext,
        bool runRunning = true,
        bool workspaceReady = true,
        bool leaseActive = true,
        bool checkpointIsCurrent = true)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Plan the next increment", Now);
        if (runRunning)
        {
            run.Claim(Now);
        }

        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        if (workspaceReady)
        {
            workspace.MarkReady();
        }

        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);

        // A distinct physical identity per call: this class's fixture shares one database
        // across every test method, and the Active-lease unique constraint is keyed by
        // (PhysicalVolumeSerialNumber, PhysicalFileId) — reusing a fixed identity here would
        // collide with another test's still-Active lease.
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);
        if (!leaseActive)
        {
            lease.Release(Now);
        }

        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.Add(attempt);

        if (!checkpointIsCurrent)
        {
            // A newer checkpoint now exists for the workspace, so the attempt's own claimed
            // checkpoint is no longer the workspace's current one.
            dbContext.GitCheckpoints.Add(
                GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 2, Now.AddMinutes(1), new string('b', 40), new string('b', 64), []));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, attempt);
    }

    [Fact]
    public async Task HandleAsync_marks_the_attempt_dispatched()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, result.Value);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    // Fail-closed contract-coherence regression, distinct from workspace/lease/checkpoint
    // eligibility: before any role-specific input query or dispatch mutation, an Agent attempt's
    // role, provider, and response contract must each be present, defined, and mutually coherent.
    // This is never provider authorization — the provider is never compared to a role-specific
    // fixed value — only a guard against malformed persisted state reaching a role branch or ever
    // being dispatched.
    [Fact]
    public async Task HandleAsync_rejects_dispatch_of_an_attempt_with_a_null_provider()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext);
        AttemptProviderSubstitution.SetProvider(attempt, (AgentProvider?)null);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_agent_contract", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
        Assert.Equal(AttemptStatus.Running, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_of_an_attempt_with_an_undefined_provider()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext);
        AttemptProviderSubstitution.SetUndefinedProvider(attempt);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_agent_contract", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_of_an_attempt_with_a_missing_role()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext);
        var roleProperty = typeof(Attempt).GetProperty(nameof(Attempt.AgentRole))!;
        roleProperty.GetSetMethod(nonPublic: true)!.Invoke(attempt, [null]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_agent_contract", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_of_an_attempt_with_a_role_response_contract_mismatch()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext);
        // attempt was claimed via ClaimAgent (Planner), so a coherent attempt would carry
        // AgentResponseContract.Proposal — CriticalReview is incoherent for this role.
        var responseContractProperty = typeof(Attempt).GetProperty(nameof(Attempt.AgentResponseContract))!;
        responseContractProperty.GetSetMethod(nonPublic: true)!.Invoke(attempt, [AgentResponseContract.CriticalReview]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("agent_attempts.invalid_agent_contract", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    // The authoritative last gate: GetEligibleAgentAttemptsQuery is only a snapshot, and
    // workspace/lease/checkpoint state can change during the pre-dispatch Git evidence capture
    // that runs between that snapshot and this call. Each of these proves one such change, alone,
    // is enough to reject dispatch with the same safe, distinguishable conflict code the
    // supervisor uses to resolve the attempt as WorkspaceNoLongerEligible — never setting the
    // dispatch marker.
    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_run_is_no_longer_running()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, runRunning: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_workspace_is_no_longer_ready()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, workspaceReady: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_lease_is_no_longer_active()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, leaseActive: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_when_the_claimed_checkpoint_is_no_longer_current()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedEligibleAttemptAsync(dbContext, checkpointIsCurrent: false);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.WorkspaceNoLongerEligibleCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_dispatched_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedAgentAttempt();
        attempt.MarkAgentDispatched(Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now.AddSeconds(1)));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.already_dispatched", Assert.Single(result.Errors).Code);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_simulated_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Simulated run", Now);
        run.Claim(Now);
        var attempt = Attempt.Claim(Guid.NewGuid(), run.Id, 1, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_agent", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_a_process_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Process run", Now);
        run.Claim(Now);
        var attempt = Attempt.ClaimProcess(
            Guid.NewGuid(), run.Id, 1,
            new ProcessExecutionIntent(@"C:\tools\build.exe", ["--verify"], @"C:\repos\devalcopilot", @"C:\repos", TimeSpan.FromMinutes(5), 65536, 131072),
            Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_agent", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task HandleAsync_fails_and_does_not_mutate_an_already_terminal_attempt()
    {
        await using var dbContext = fixture.CreateContext();
        var (project, run, attempt) = CreateClaimedAgentAttempt();
        attempt.CompleteAgent(AgentOutcome.ProviderInvocationFailed, null, Now);
        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.Attempts.Add(attempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_active", Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_belongs_to_a_different_run()
    {
        await using var dbContext = fixture.CreateContext();
        var (targetProject, targetRun, _) = CreateClaimedAgentAttempt();
        var (otherProject, otherRun, otherAttempt) = CreateClaimedAgentAttempt();
        dbContext.Projects.AddRange(targetProject, otherProject);
        dbContext.Runs.AddRange(targetRun, otherRun);
        dbContext.Attempts.Add(otherAttempt);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(targetRun.Id, otherAttempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
        Assert.Null(otherAttempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_fails_when_the_attempt_does_not_exist()
    {
        await using var dbContext = fixture.CreateContext();

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("attempts.not_found", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// The critical-review-specific half of the same authoritative last gate: a competing
    /// critical-review attempt can have committed a successful (Accepted/Challenged) review of
    /// the exact same reviewed Proposal strictly between the eligibility snapshot and this call —
    /// this attempt must never be dispatched to review a Proposal that already has a successful
    /// review, reported with the same safe <c>WorkspaceNoLongerEligibleCode</c> conflict as every
    /// other last-gate rejection.
    /// </summary>
    [Fact]
    public async Task HandleAsync_rejects_dispatch_of_a_critical_review_attempt_when_the_same_proposal_already_has_a_successful_review()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the next increment", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);

        var reviewedProposalId = Guid.NewGuid();
        var attempt = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);

        // A competing critical-review attempt already recorded a successful review of the exact
        // same Proposal message — this one must never be dispatched.
        var competingReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        competingReview.MarkAgentDispatched(Now);
        competingReview.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.AddRange(attempt, competingReview);
        dbContext.AttemptInputMessages.AddRange(
            AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, reviewedProposalId, sequence: 0),
            AttemptInputMessage.Record(Guid.NewGuid(), competingReview.Id, reviewedProposalId, sequence: 0));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InputAlreadyReviewedCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    /// <summary>
    /// The already-reviewed check is scoped to <see cref="AgentRole.CriticalReviewer"/> attempts
    /// only — a Codex planning attempt is dispatched normally even while another attempt in the
    /// database (a critical review that already succeeded) exists, since Codex planning attempts
    /// have no reviewed-Proposal identity for that check to ever apply to.
    /// </summary>
    [Fact]
    public async Task HandleAsync_dispatches_a_codex_planning_attempt_unaffected_by_the_already_reviewed_check()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, planningAttempt) = await SeedEligibleAttemptAsync(dbContext);

        var reviewedProposalId = Guid.NewGuid();
        var unrelatedCompletedReview = Attempt.ClaimAgentCriticalReview(
            Guid.NewGuid(), run.Id, 99, Guid.NewGuid(), Guid.NewGuid(), Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        unrelatedCompletedReview.MarkAgentDispatched(Now);
        unrelatedCompletedReview.CompleteAgent(AgentOutcome.Accepted, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);
        dbContext.Attempts.Add(unrelatedCompletedReview);
        dbContext.AttemptInputMessages.Add(
            AttemptInputMessage.Record(Guid.NewGuid(), unrelatedCompletedReview.Id, reviewedProposalId, sequence: 0));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, planningAttempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, planningAttempt.AgentDispatchedAtUtc);
    }

    /// <summary>Seeds one fully eligible, undispatched Resolver attempt whose ordered input set
    /// (original Proposal at sequence 0, then every Challenge in order) is exactly
    /// <paramref name="ownInputIds"/>, plus one already-Completed/Resolved competing Resolver
    /// attempt whose own ordered input set is exactly <paramref name="competingInputIds"/> —
    /// deliberately allowed to differ in length, membership, or order from
    /// <paramref name="ownInputIds"/> so a test can prove the dispatch-time gate compares the
    /// complete ordered sequence, never a set/overlap check.</summary>
    private static async Task<(Run Run, Attempt Attempt)> SeedResolverAttemptWithCompetingResolutionAsync(
        DevalCopilotDbContext dbContext, IReadOnlyList<Guid> ownInputIds, IReadOnlyList<Guid> competingInputIds)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Resolve the challenged proposal", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);

        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);

        var competingResolution = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        competingResolution.MarkAgentDispatched(Now);
        competingResolution.CompleteAgent(AgentOutcome.Resolved, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.AddRange(attempt, competingResolution);

        for (var index = 0; index < ownInputIds.Count; index++)
        {
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, ownInputIds[index], sequence: index));
        }

        for (var index = 0; index < competingInputIds.Count; index++)
        {
            dbContext.AttemptInputMessages.Add(
                AttemptInputMessage.Record(Guid.NewGuid(), competingResolution.Id, competingInputIds[index], sequence: index));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, attempt);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_of_a_challenge_resolution_attempt_when_the_exact_same_ordered_input_set_already_has_a_successful_resolution()
    {
        await using var dbContext = fixture.CreateContext();
        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();
        var (run, attempt) = await SeedResolverAttemptWithCompetingResolutionAsync(
            dbContext,
            ownInputIds: [proposalId, challenge1Id, challenge2Id],
            competingInputIds: [proposalId, challenge1Id, challenge2Id]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InputAlreadyResolvedCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_normally_when_a_prior_resolution_only_partially_covers_the_input_set()
    {
        await using var dbContext = fixture.CreateContext();
        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();
        var (run, attempt) = await SeedResolverAttemptWithCompetingResolutionAsync(
            dbContext,
            ownInputIds: [proposalId, challenge1Id, challenge2Id],
            // A prior resolution of only the first challenge — a partial match must never block.
            competingInputIds: [proposalId, challenge1Id]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_normally_when_a_prior_resolution_belongs_to_an_entirely_foreign_input_set()
    {
        await using var dbContext = fixture.CreateContext();
        var (run, attempt) = await SeedResolverAttemptWithCompetingResolutionAsync(
            dbContext,
            ownInputIds: [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()],
            // A different Proposal and different Challenges entirely — no shared identity at all.
            competingInputIds: [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_normally_when_a_prior_resolution_has_the_identical_elements_in_a_different_order()
    {
        await using var dbContext = fixture.CreateContext();
        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();
        var (run, attempt) = await SeedResolverAttemptWithCompetingResolutionAsync(
            dbContext,
            ownInputIds: [proposalId, challenge1Id, challenge2Id],
            // The identical three ids, but the two Challenges swapped — same set, different
            // sequence. Never a false match: this is not the same ordered input identity.
            competingInputIds: [proposalId, challenge2Id, challenge1Id]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_normally_when_a_prior_resolution_merely_overlaps_with_one_extra_challenge()
    {
        await using var dbContext = fixture.CreateContext();
        var proposalId = Guid.NewGuid();
        var challenge1Id = Guid.NewGuid();
        var challenge2Id = Guid.NewGuid();
        var (run, attempt) = await SeedResolverAttemptWithCompetingResolutionAsync(
            dbContext,
            ownInputIds: [proposalId, challenge1Id, challenge2Id],
            // A superset — the same two challenges plus a third that never belonged to this
            // attempt's own review. Overlap is never enough to match.
            competingInputIds: [proposalId, challenge1Id, challenge2Id, Guid.NewGuid()]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(
            new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }

    /// <summary>Seeds one fully eligible, undispatched CodeReviewer attempt whose exact input
    /// identity (the reviewed ExecutionReport plus its ordered claimed verification-execution set)
    /// is exactly <paramref name="ownExecutionIds"/>, plus one already-Completed/ReviewApproved
    /// competing CodeReviewer attempt whose own claimed set is exactly
    /// <paramref name="competingExecutionIds"/> — deliberately allowed to differ in length,
    /// membership, or order so a test can prove the dispatch-time gate compares the complete
    /// ordered sequence on both parts, never a set/overlap check.</summary>
    private static async Task<(Run Run, Attempt Attempt)> SeedCodeReviewAttemptWithCompetingReviewAsync(
        DevalCopilotDbContext dbContext,
        Guid executionReportMessageId,
        Guid competingExecutionReportMessageId,
        IReadOnlyList<Guid> ownExecutionIds,
        IReadOnlyList<Guid> competingExecutionIds)
    {
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Review the implementation", Now);
        run.Claim(Now);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", Now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, Now, new string('a', 40), Fingerprint, []);
        var lease = RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), Now);

        var attempt = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 1, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);

        var competingReview = Attempt.ClaimAgentCodeReview(
            Guid.NewGuid(), run.Id, 2, workspace.Id, checkpoint.Id, Fingerprint, Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 262144, 524288, Now);
        competingReview.MarkAgentDispatched(Now);
        competingReview.CompleteAgent(AgentOutcome.ReviewApproved, Fingerprint, Now, processEvidence: TestProcessEvidence.CleanExit);

        dbContext.Projects.Add(project);
        dbContext.Runs.Add(run);
        dbContext.GitWorkspaces.Add(workspace);
        dbContext.GitCheckpoints.Add(checkpoint);
        dbContext.RepositoryMutationLeases.Add(lease);
        dbContext.Attempts.AddRange(attempt, competingReview);

        dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, executionReportMessageId, sequence: 0));
        dbContext.AttemptInputMessages.Add(
            AttemptInputMessage.Record(Guid.NewGuid(), competingReview.Id, competingExecutionReportMessageId, sequence: 0));

        // One distinct VerificationCommand per distinct execution: attempt_verification_evidence
        // enforces uniqueness on (AttemptId, VerificationCommandId), so two evidence rows on the
        // same attempt can never legitimately share a command.
        var allExecutionIds = ownExecutionIds.Concat(competingExecutionIds).Distinct().ToList();
        var commandIdByExecutionId = new Dictionary<Guid, Guid>();
        var executions = new List<VerificationExecution>();
        var commands = new List<VerificationCommand>();
        for (var index = 0; index < allExecutionIds.Count; index++)
        {
            var command = VerificationCommand.Configure(
                Guid.NewGuid(), project.Id, index + 1, $"Command {index + 1}", @"C:\dotnet.exe", ["test"], 300, true, Now);
            var execution = VerificationExecution.Claim(allExecutionIds[index], project.Id, index + 1, workspace, checkpoint, command, Now);
            execution.MarkDispatched(Now);
            execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, Now);
            commandIdByExecutionId[allExecutionIds[index]] = command.Id;
            commands.Add(command);
            executions.Add(execution);
        }

        dbContext.VerificationCommands.AddRange(commands);
        dbContext.VerificationExecutions.AddRange(executions);

        for (var index = 0; index < ownExecutionIds.Count; index++)
        {
            dbContext.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.Record(
                Guid.NewGuid(), attempt.Id, commandIdByExecutionId[ownExecutionIds[index]], ownExecutionIds[index], sequence: index));
        }

        for (var index = 0; index < competingExecutionIds.Count; index++)
        {
            dbContext.AttemptVerificationEvidence.Add(AttemptVerificationEvidence.Record(
                Guid.NewGuid(), competingReview.Id, commandIdByExecutionId[competingExecutionIds[index]], competingExecutionIds[index], sequence: index));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return (run, attempt);
    }

    [Fact]
    public async Task HandleAsync_rejects_dispatch_of_a_code_review_attempt_when_the_exact_same_input_identity_already_has_a_successful_review()
    {
        await using var dbContext = fixture.CreateContext();
        var executionReportMessageId = Guid.NewGuid();
        var executionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (run, attempt) = await SeedCodeReviewAttemptWithCompetingReviewAsync(
            dbContext, executionReportMessageId, executionReportMessageId, executionIds, executionIds);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MarkAgentAttemptDispatchedCommandHandler.InputAlreadyCodeReviewedCode, Assert.Single(result.Errors).Code);
        Assert.Null(attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_a_code_review_attempt_normally_when_a_prior_review_covers_a_different_execution_report()
    {
        await using var dbContext = fixture.CreateContext();
        var executionIds = new[] { Guid.NewGuid() };
        var (run, attempt) = await SeedCodeReviewAttemptWithCompetingReviewAsync(
            dbContext, Guid.NewGuid(), Guid.NewGuid(), executionIds, executionIds);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }

    [Fact]
    public async Task HandleAsync_dispatches_a_code_review_attempt_normally_when_a_prior_review_only_partially_covers_the_verification_set()
    {
        await using var dbContext = fixture.CreateContext();
        var executionReportMessageId = Guid.NewGuid();
        var ownExecutionIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var (run, attempt) = await SeedCodeReviewAttemptWithCompetingReviewAsync(
            dbContext, executionReportMessageId, executionReportMessageId, ownExecutionIds, [ownExecutionIds[0]]);

        var handler = new MarkAgentAttemptDispatchedCommandHandler(dbContext, new FixedTimeProvider(Now));
        var result = await handler.HandleAsync(new MarkAgentAttemptDispatchedCommand(run.Id, attempt.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now, attempt.AgentDispatchedAtUtc);
    }
}
