using DevalCopilot.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Persistence;

/// <summary>
/// Proves the <c>AddCheckpointReviewEvidenceMembership</c> migration backfills every existing
/// decided review's legacy single-execution snapshot into exactly one
/// <c>checkpoint_review_evidence</c> row without data loss, and that the legacy snapshot columns
/// no longer exist afterward — never retained alongside the new relational evidence set.
/// </summary>
public sealed class CheckpointReviewEvidenceMembershipMigrationTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-checkpoint-review-evidence-migration-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_databasePath}"));
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Backfill_moves_every_existing_decided_reviews_snapshot_into_exactly_one_evidence_row()
    {
        const string PriorMigration = "20260918214858_AddAgentResultGitCheckpoint";

        await using (var context = CreateContext())
        {
            // Migrate only up to immediately before this slice's migration, so the legacy
            // single-execution snapshot columns still exist to seed against.
            await context.Database.GetService<IMigrator>().MigrateAsync(PriorMigration);
        }

        var (projectId, workspaceId, checkpointId, commandId, executionId, decidedReviewId, pendingReviewId) = await SeedLegacyRowsAsync();

        await using (var probe = new SqliteConnection($"Data Source={_databasePath}"))
        {
            await probe.OpenAsync();
            await using var probeCommand = probe.CreateCommand();
            probeCommand.CommandText = "SELECT COUNT(*) FROM checkpoint_reviews;";
            var count = (long)(await probeCommand.ExecuteScalarAsync())!;
            if (count != 2)
            {
                throw new InvalidOperationException($"Expected 2 seeded checkpoint_reviews rows before migrating forward, found {count}.");
            }
        }

        await using (var context = CreateContext())
        {
            // Now apply every remaining migration, including this slice's own.
            await context.Database.MigrateAsync();
        }

        await using var verifyContext = CreateContext();
        var allReviews = await verifyContext.CheckpointReviews
            .Include(review => review.Evidence)
            .ToListAsync();
        Assert.True(
            allReviews.Count == 2,
            $"decidedReviewId={decidedReviewId}; pendingReviewId={pendingReviewId}; found=[{string.Join(", ", allReviews.Select(r => r.Id))}]");
        var decidedReview = allReviews.Single(review => review.Id == decidedReviewId);
        var pendingReview = allReviews.Single(review => review.Id == pendingReviewId);

        var evidence = Assert.Single(decidedReview.Evidence);
        Assert.Equal(executionId, evidence.VerificationExecutionId);
        Assert.Equal(commandId, evidence.VerificationCommandId);
        Assert.Equal(1, evidence.VerificationExecutionNumber);
        Assert.Equal(Fingerprint, evidence.VerificationExecutionCheckpointFingerprintSha256);
        Assert.Equal(Domain.Features.Projects.VerificationExecutionStatus.Passed, evidence.VerificationExecutionStatus);
        Assert.Equal(Domain.Features.Projects.VerificationExecutionOutcome.Exited, evidence.VerificationExecutionOutcome);

        Assert.Empty(pendingReview.Evidence);

        // The legacy columns genuinely no longer exist — never retained alongside the new
        // relational evidence set.
        await using var rawConnection = new SqliteConnection($"Data Source={_databasePath}");
        await rawConnection.OpenAsync();
        await using var command = rawConnection.CreateCommand();
        command.CommandText = "PRAGMA table_info(checkpoint_reviews);";
        await using var reader = await command.ExecuteReaderAsync();
        var columnNames = new List<string>();
        while (await reader.ReadAsync())
        {
            columnNames.Add(reader.GetString(1));
        }

        Assert.DoesNotContain("VerificationExecutionId", columnNames);
        Assert.DoesNotContain("VerificationExecutionStatus", columnNames);
        Assert.DoesNotContain("VerificationExecutionOutcome", columnNames);
        Assert.DoesNotContain("VerificationExecutionExitCode", columnNames);
        Assert.DoesNotContain("VerificationExecutionNumber", columnNames);
        Assert.DoesNotContain("VerificationExecutionCheckpointFingerprintSha256", columnNames);
    }

    private DevalCopilotDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DevalCopilotDbContext>().UseSqlite($"Data Source={_databasePath}").Options);

    private const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>Seeds rows directly via raw SQL against the pre-migration legacy schema — the
    /// C# <c>CheckpointReview</c>/<c>VerificationExecution</c> types in this checkout no longer
    /// have the legacy shape, so EF entities cannot be used to seed it.</summary>
    private async Task<(Guid ProjectId, Guid WorkspaceId, Guid CheckpointId, Guid CommandId, Guid ExecutionId, Guid DecidedReviewId, Guid PendingReviewId)>
        SeedLegacyRowsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var decidedReviewId = Guid.NewGuid();
        var pendingReviewId = Guid.NewGuid();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO projects (Id, Name, CanonicalPath, NextExecutionNumber, NextBaselineNumber, RegistrationIdentityKey)
                VALUES ($projectId, 'Migration test project', $repoPath, 1, 1, $registrationIdentityKey);

                INSERT INTO git_workspaces (Id, ProjectId, WorkspaceNumber, WorkspacePath, BranchName, SourceCommitSha, Status, CreatedAtUtc, NextCheckpointNumber)
                VALUES ($workspaceId, $projectId, 1, $workspacePath, 'branch', $sha40, 'Ready', $now, 2);

                INSERT INTO git_checkpoints (Id, WorkspaceId, CheckpointNumber, CapturedAtUtc, HeadCommitSha, FingerprintSha256)
                VALUES ($checkpointId, $workspaceId, 1, $now, $sha40, $fingerprint);

                INSERT INTO verification_commands (Id, ProjectId, CommandNumber, Name, ExecutablePath, Arguments, TimeoutSeconds, IsEnabled, ConfiguredAtUtc, UpdatedAtUtc)
                VALUES ($commandId, $projectId, 1, 'Tests', $execPath, '[]', 60, 1, $now, $now);

                INSERT INTO verification_executions
                    (Id, ProjectId, ExecutionNumber, GitWorkspaceId, GitCheckpointId, VerificationCommandId, WorkspacePath, CheckpointFingerprintSha256,
                     CommandName, ExecutablePath, Arguments, TimeoutSeconds, Status, ClaimedAtUtc, DispatchedAtUtc, CompletedAtUtc, Outcome, ExitCode, CompletionFingerprintSha256)
                VALUES
                    ($executionId, $projectId, 1, $workspaceId, $checkpointId, $commandId, $workspacePath, $fingerprint,
                     'Tests', $execPath, '[]', 60, 'Passed', $now, $now, $now, 'Exited', 0, $fingerprint);

                INSERT INTO checkpoint_reviews
                    (Id, ProjectId, GitWorkspaceId, GitCheckpointId, CheckpointNumber, CheckpointFingerprintSha256,
                     VerificationExecutionId, VerificationExecutionNumber, VerificationExecutionCheckpointFingerprintSha256,
                     VerificationExecutionStatus, VerificationExecutionOutcome, VerificationExecutionExitCode,
                     ActorKind, Decision, RecordedAtUtc, RecordedAtUtcTicks)
                VALUES
                    ($decidedReviewId, $projectId, $workspaceId, $checkpointId, 1, $fingerprint,
                     $executionId, 1, $fingerprint,
                     'Passed', 'Exited', 0,
                     'Human', 'Approved', $now, 0);

                INSERT INTO checkpoint_reviews
                    (Id, ProjectId, GitWorkspaceId, GitCheckpointId, CheckpointNumber, CheckpointFingerprintSha256,
                     VerificationExecutionId, VerificationExecutionNumber, VerificationExecutionCheckpointFingerprintSha256,
                     VerificationExecutionStatus, VerificationExecutionOutcome, VerificationExecutionExitCode,
                     ActorKind, Decision, RecordedAtUtc, RecordedAtUtcTicks)
                VALUES
                    ($pendingReviewId, $projectId, $workspaceId, $checkpointId, 1, $fingerprint,
                     NULL, NULL, NULL,
                     NULL, NULL, NULL,
                     'Human', 'Pending', $now, 1);
                """;
            command.Parameters.AddWithValue("$projectId", projectId.ToString());
            command.Parameters.AddWithValue("$workspaceId", workspaceId.ToString());
            command.Parameters.AddWithValue("$checkpointId", checkpointId.ToString());
            command.Parameters.AddWithValue("$commandId", commandId.ToString());
            command.Parameters.AddWithValue("$executionId", executionId.ToString());
            command.Parameters.AddWithValue("$decidedReviewId", decidedReviewId.ToString());
            command.Parameters.AddWithValue("$pendingReviewId", pendingReviewId.ToString());
            command.Parameters.AddWithValue("$repoPath", $@"C:\repos\{Guid.NewGuid():N}");
            command.Parameters.AddWithValue("$registrationIdentityKey", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$workspacePath", $@"C:\workspaces\{Guid.NewGuid():N}");
            command.Parameters.AddWithValue("$execPath", @"C:\dotnet.exe");
            command.Parameters.AddWithValue("$sha40", new string('a', 40));
            command.Parameters.AddWithValue("$fingerprint", Fingerprint);
            command.Parameters.AddWithValue("$now", now.ToString("O"));

            await command.ExecuteNonQueryAsync();
        }

        return (projectId, workspaceId, checkpointId, commandId, executionId, decidedReviewId, pendingReviewId);
    }
}
