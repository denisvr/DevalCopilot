using DevalCopilot.Application.Features.Runs.Commands.RecordGitHubBranchPublicationApproval;
using DevalCopilot.Application.Features.Runs.Errors;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchPublicationApproval;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The scope-independent behavior of the explicit branch publication approval (ADR-0048) on a real file-backed SQLite database: an unknown
/// run, a run with nothing recorded, requests outside the contract, and an approval row or event that has no coherent scope to belong to
/// (an orphan is never "not recorded" and never a replay), with nothing written in any of those cases. The coherent paths need a real
/// completed delivery with a recorded target and binding and are proved through the production host in the Api integration tests.
/// </summary>
public sealed class GitHubBranchPublicationApprovalHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Sha = new('d', 40);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Devalente.Shared.Results.Result<GetGitHubBranchPublicationApprovalQueryResult>> QueryAsync(Guid runId)
    {
        await using var db = _fixture.CreateContext();
        return await new GetGitHubBranchPublicationApprovalQueryHandler(db)
            .HandleAsync(new GetGitHubBranchPublicationApprovalQuery(runId), CancellationToken.None);
    }

    private async Task<Devalente.Shared.Results.Result<RecordGitHubBranchPublicationApprovalCommandResult>> RecordAsync(
        RecordGitHubBranchPublicationApprovalCommand command)
    {
        await using var db = _fixture.CreateContext();
        return await new RecordGitHubBranchPublicationApprovalCommandHandler(db, new FixedTimeProvider(Now))
            .HandleAsync(command, CancellationToken.None);
    }

    private static RecordGitHubBranchPublicationApprovalCommand CommandFor(Guid runId) => new(
        runId, Guid.NewGuid(), Guid.NewGuid(), Sha, "devalcopilot/workspace/x/1", Guid.NewGuid(), Guid.NewGuid(), "R_kgDOExample01");

    private async Task<string> FingerprintAsync()
    {
        await using var db = _fixture.CreateContext();
        return $"{await db.Runs.CountAsync()}|{await db.Events.CountAsync()}|{await db.GitHubObservationTargets.CountAsync()}"
            + $"|{await db.GitHubRepositoryIdentityBindings.CountAsync()}|{await db.GitHubBranchPublicationApprovals.CountAsync()}";
    }

    private async Task<Guid> SeedBareRunAsync()
    {
        var runId = Guid.NewGuid();
        await using var db = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Approval", $@"C:\repos\{Guid.NewGuid():N}", Now);
        db.Projects.Add(project);
        db.Runs.Add(Run.RecordIntent(runId, project.Id, project.ReserveExecutionNumber(), "Deliver", Now));
        await db.SaveChangesAsync();
        return runId;
    }

    /// <summary>A delivered operation with its target and binding rows and events but no receipt lineage, so the scope is not admissible, plus
    /// an approval row and event naming them.</summary>
    private async Task<(Guid RunId, GitHubBranchPublicationApproval Approval)> SeedOrphanApprovalAsync(bool row, bool recordEvent)
    {
        await using var db = _fixture.CreateContext();
        var rows = await LocalCommitRowsSeed.SeedAsync(db, LocalCommitStatus.Completed, WorkspaceStatus.Ready);
        var target = GitHubObservationTarget.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "octo-cat", "hello.world", LocalCommitRowsSeed.Now, Guid.NewGuid());
        db.GitHubObservationTargets.Add(target);
        db.Events.Add(RunEvent.Record(
            target.EventId, rows.Run.Id, null, RunEventType.GitHubObservationTargetRecorded, ParticipantIdentity.ForHuman(),
            GitHubObservationTargetEventPayload.Build(target), target.RecordedAtUtc));
        var binding = GitHubRepositoryIdentityBinding.Record(
            Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, target.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, "R_kgDOExample01", "octo-cat", "hello.world", LocalCommitRowsSeed.Now,
            LocalCommitRowsSeed.Now.AddSeconds(1), LocalCommitRowsSeed.Now.AddSeconds(2), Guid.NewGuid());
        db.GitHubRepositoryIdentityBindings.Add(binding);
        db.Events.Add(RunEvent.Record(
            binding.EventId, rows.Run.Id, null, RunEventType.GitHubRepositoryIdentityBound, ParticipantIdentity.ForHuman(),
            GitHubRepositoryIdentityBindingEventPayload.Build(binding), binding.RecordedAtUtc));
        var approval = GitHubBranchPublicationApproval.Record(
            Guid.NewGuid(), Guid.NewGuid(), rows.Run.Id, rows.Project.Id, rows.Operation.Id, rows.Workspace.Id, rows.Operation.CommitSha,
            rows.Operation.BranchName, target.Id, target.EventId, target.Owner, target.Repository, target.RecordedAtUtc, binding.Id,
            binding.EventId, binding.RepositoryNodeId, binding.Owner, binding.Repository, binding.ObservedAtUtc, binding.RecordedAtUtc,
            binding.RecordedAtUtc.AddSeconds(1));
        if (row)
        {
            db.GitHubBranchPublicationApprovals.Add(approval);
        }

        if (recordEvent)
        {
            db.Events.Add(RunEvent.Record(
                approval.EventId, rows.Run.Id, null, RunEventType.GitHubBranchPublicationApproved, ParticipantIdentity.ForHuman(),
                GitHubBranchPublicationApprovalEventPayload.Build(approval), approval.ApprovedAtUtc));
        }

        await db.SaveChangesAsync();
        return (rows.Run.Id, approval);
    }

    [Fact]
    public async Task An_unknown_run_is_the_fixed_not_found_for_the_read_and_the_write()
    {
        var read = await QueryAsync(Guid.NewGuid());
        var write = await RecordAsync(CommandFor(Guid.NewGuid()));

        Assert.True(read.IsFailure);
        Assert.Equal("github_branch_publication_approval.not_found", Assert.Single(read.Errors).Code);
        Assert.True(write.IsFailure);
        Assert.Equal(GitHubBranchPublicationApprovalErrors.NotFoundCode, Assert.Single(write.Errors).Code);
    }

    [Fact]
    public async Task A_run_with_nothing_recorded_is_not_recorded_with_no_fact_and_the_write_is_unavailable_and_writes_nothing()
    {
        var runId = await SeedBareRunAsync();
        var before = await FingerprintAsync();

        var read = await QueryAsync(runId);
        var write = await RecordAsync(CommandFor(runId));

        Assert.True(read.IsSuccess);
        Assert.Equal(GitHubBranchPublicationApprovalStatus.NotRecorded, read.Value.Status);
        Assert.Equal(runId, read.Value.RunId);
        Assert.Null(read.Value.Source);
        Assert.Null(read.Value.Target);
        Assert.Null(read.Value.Binding);
        Assert.Null(read.Value.Approval);
        Assert.True(write.IsFailure);
        Assert.Equal(GitHubBranchPublicationApprovalErrors.UnavailableCode, Assert.Single(write.Errors).Code);
        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public async Task A_request_outside_the_contract_is_the_fixed_invalid_request_and_writes_nothing()
    {
        var runId = await SeedBareRunAsync();
        var before = await FingerprintAsync();
        var valid = CommandFor(runId);

        foreach (var command in new[]
                 {
                     valid with { RunId = Guid.Empty },
                     valid with { ApprovalId = Guid.Empty },
                     valid with { OperationId = Guid.Empty },
                     valid with { TargetId = Guid.Empty },
                     valid with { BindingId = Guid.Empty },
                     valid with { CommitSha = new string('D', 40) },
                     valid with { CommitSha = "abc" },
                     valid with { BranchName = string.Empty },
                     valid with { BranchName = new string('b', GitHubObservationTarget.MaximumBranchNameLength + 1) },
                     valid with { RepositoryNodeId = "has space" },
                     valid with { RepositoryNodeId = string.Empty },
                 })
        {
            var write = await RecordAsync(command);

            Assert.True(write.IsFailure);
            Assert.Equal(GitHubBranchPublicationApprovalErrors.InvalidRequestCode, Assert.Single(write.Errors).Code);
        }

        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task An_approval_row_or_event_with_no_admissible_scope_is_unavailable_never_not_recorded_or_a_replay(bool row, bool recordEvent)
    {
        var (runId, approval) = await SeedOrphanApprovalAsync(row, recordEvent);
        var before = await FingerprintAsync();

        var read = await QueryAsync(runId);
        var write = await RecordAsync(CommandFor(runId) with { ApprovalId = approval.Id });

        Assert.True(read.IsSuccess);
        Assert.Equal(GitHubBranchPublicationApprovalStatus.Unavailable, read.Value.Status);
        Assert.Null(read.Value.Source);
        Assert.Null(read.Value.Target);
        Assert.Null(read.Value.Binding);
        Assert.Null(read.Value.Approval);
        Assert.True(write.IsFailure);
        Assert.Equal(GitHubBranchPublicationApprovalErrors.UnavailableCode, Assert.Single(write.Errors).Code);
        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public async Task The_statuses_are_exactly_the_four_closed_names()
    {
        await Task.CompletedTask;

        Assert.Equal(
            ["NotRecorded", "ApprovalNotRecorded", "Available", "Unavailable"],
            Enum.GetNames<GitHubBranchPublicationApprovalStatus>());
    }

    [Fact]
    public void The_fixed_error_codes_are_the_six_published_ones()
    {
        Assert.Equal(
            [
                "github_branch_publication_approval.invalid_request",
                "github_branch_publication_approval.body_not_allowed",
                "github_branch_publication_approval.body_too_large",
                "github_branch_publication_approval.not_found",
                "github_branch_publication_approval.unavailable",
                "github_branch_publication_approval.conflict",
            ],
            [
                GitHubBranchPublicationApprovalErrors.InvalidRequestCode,
                GitHubBranchPublicationApprovalErrors.BodyNotAllowedCode,
                GitHubBranchPublicationApprovalErrors.BodyTooLargeCode,
                GitHubBranchPublicationApprovalErrors.NotFoundCode,
                GitHubBranchPublicationApprovalErrors.UnavailableCode,
                GitHubBranchPublicationApprovalErrors.ConflictCode,
            ]);
        foreach (var error in new[]
                 {
                     GitHubBranchPublicationApprovalErrors.InvalidRequest(),
                     GitHubBranchPublicationApprovalErrors.BodyNotAllowed(),
                     GitHubBranchPublicationApprovalErrors.BodyTooLarge(),
                     GitHubBranchPublicationApprovalErrors.NotFound(),
                     GitHubBranchPublicationApprovalErrors.Unavailable(),
                     GitHubBranchPublicationApprovalErrors.Conflict(),
                 })
        {
            Assert.DoesNotContain("R_kgDO", error.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("devalcopilot/", error.Description, StringComparison.Ordinal);
        }
    }
}
