using System.Net;
using System.Text.Json;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// ADR-0033: the project run history locates the receipt of a run's own recorded Completed local commit, from real deliveries
/// written by the production handlers and supervisor against real Git and file-backed SQLite. The source only locates a receipt; it
/// is resolved by the run's own operation, never by the latest operation, the current checkpoint or another run, and a source that
/// cannot be coherently identified is null rather than a guess.
/// </summary>
public sealed class ProjectRunHistoryReceiptSourceTests : LocalDeliveryReceiptTestBase
{
    private static readonly string[] PageKeys = ["projectId", "entries", "hasMore", "nextBeforeExecutionNumber"];

    private static readonly string[] EntryKeys =
    [
        "projectId", "runId", "executionNumber", "objective", "lifecycle", "stage", "executionMode", "createdAtUtc", "lastAdvancedAtUtc",
        "receiptSource",
    ];

    private static readonly string[] SourceKeys = ["runId", "operationId", "commitSha", "checkpointId", "checkpointNumber"];

    private static async Task<(HttpStatusCode Status, JsonElement Page, string Text)> HistoryAsync(
        LocalCommitHost host, Guid projectId, string? query = null)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.GetAsync($"/api/projects/{projectId}/run-history{(query is null ? string.Empty : "?" + query)}");
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private static JsonElement EntryOf(JsonElement page, Guid runId) =>
        page.GetProperty("entries").EnumerateArray().Single(entry => entry.GetProperty("runId").GetGuid() == runId);

    [Fact]
    public async Task A_delivered_run_is_located_by_its_own_operation_and_a_later_undelivered_run_has_no_source()
    {
        using var host = StartHost(runSupervisor: true);
        var (delivered, operationId) = await DeliverAsync(host);
        var later = await LocalCommitLineage.SeedAsync(host, Scene, continueFrom: delivered);

        var history = await HistoryAsync(host, delivered.ProjectId);

        Assert.Equal(HttpStatusCode.OK, history.Status);
        Assert.Equal(PageKeys.Order(StringComparer.Ordinal), history.Page.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, history.Page.GetProperty("entries").GetArrayLength());
        Assert.False(history.Page.GetProperty("hasMore").GetBoolean());

        var operation = await OperationByIdAsync(operationId);
        var entry = EntryOf(history.Page, delivered.RunId);
        Assert.Equal(EntryKeys.Order(StringComparer.Ordinal), entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Completed", entry.GetProperty("lifecycle").GetString());
        Assert.Equal("Completed", entry.GetProperty("stage").GetString());
        Assert.Equal("ManualAgent", entry.GetProperty("executionMode").GetString());
        Assert.Equal("Implement the ledger", entry.GetProperty("objective").GetString());

        var source = entry.GetProperty("receiptSource");
        Assert.Equal(SourceKeys.Order(StringComparer.Ordinal), source.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(delivered.RunId, source.GetProperty("runId").GetGuid());
        Assert.Equal(operationId, source.GetProperty("operationId").GetGuid());
        Assert.Equal(operation.CommitSha, source.GetProperty("commitSha").GetString());
        Assert.Equal(Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim(), source.GetProperty("commitSha").GetString());
        Assert.Equal(delivered.CheckpointId, source.GetProperty("checkpointId").GetGuid());
        Assert.Equal(operation.CheckpointNumber, source.GetProperty("checkpointNumber").GetInt32());

        // The later run is the newest entry and shares the project, workspace and lease, yet has no operation of its own.
        Assert.Equal(later.RunId, history.Page.GetProperty("entries")[0].GetProperty("runId").GetGuid());
        Assert.Equal(JsonValueKind.Null, EntryOf(history.Page, later.RunId).GetProperty("receiptSource").ValueKind);

        // Nothing but the identity-level source leaks: no path, author address, commit message, authority digest or index artifact.
        Assert.DoesNotContain(Scene.MainPath, history.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Scene.WorkspacePath, history.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(operation.AuthorEmail, history.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(operation.NormalizedMessage, history.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(operation.CheckpointFingerprintSha256, history.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(operation.TreeSha, history.Text, StringComparison.Ordinal);

        // The located receipt is the one the receipt endpoint reconstructs for exactly that source.
        var receipt = (await ReceiptAsync(host, delivered.RunId)).Receipt;
        Assert.Equal(source.GetProperty("runId").GetGuid(), receipt.GetProperty("runId").GetGuid());
        Assert.Equal(source.GetProperty("operationId").GetGuid(), receipt.GetProperty("operationId").GetGuid());
        Assert.Equal(source.GetProperty("commitSha").GetString(), receipt.GetProperty("commitSha").GetString());
        Assert.Equal(source.GetProperty("checkpointId").GetGuid(), receipt.GetProperty("checkpoint").GetProperty("id").GetGuid());
        Assert.Equal(source.GetProperty("checkpointNumber").GetInt32(), receipt.GetProperty("checkpoint").GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task Each_delivered_run_keeps_its_own_pinned_source_after_a_later_run_is_delivered()
    {
        using var host = StartHost(runSupervisor: true);
        var (first, firstOperation) = await DeliverAsync(host);
        var beforeLater = await HistoryAsync(host, first.ProjectId);
        var firstSourceBefore = EntryOf(beforeLater.Page, first.RunId).GetProperty("receiptSource").GetRawText();

        var (second, secondOperation) = await DeliverAsync(host, continueFrom: first);

        var history = await HistoryAsync(host, first.ProjectId);
        Assert.Equal(2, history.Page.GetProperty("entries").GetArrayLength());
        var firstSource = EntryOf(history.Page, first.RunId).GetProperty("receiptSource");
        var secondSource = EntryOf(history.Page, second.RunId).GetProperty("receiptSource");

        Assert.NotEqual(firstOperation, secondOperation);
        Assert.Equal(firstOperation, firstSource.GetProperty("operationId").GetGuid());
        Assert.Equal(secondOperation, secondSource.GetProperty("operationId").GetGuid());
        Assert.Equal(first.RunId, firstSource.GetProperty("runId").GetGuid());
        Assert.Equal(second.RunId, secondSource.GetProperty("runId").GetGuid());
        Assert.NotEqual(firstSource.GetProperty("commitSha").GetString(), secondSource.GetProperty("commitSha").GetString());
        Assert.NotEqual(firstSource.GetProperty("checkpointId").GetGuid(), secondSource.GetProperty("checkpointId").GetGuid());
        Assert.Equal(firstSourceBefore, firstSource.GetRawText());

        // The older receipt is still the older delivery, read by its own run identity.
        var older = (await ReceiptAsync(host, first.RunId)).Receipt;
        Assert.Equal(firstSource.GetProperty("commitSha").GetString(), older.GetProperty("commitSha").GetString());
        Assert.Equal(firstSource.GetProperty("operationId").GetGuid(), older.GetProperty("operationId").GetGuid());
    }

    [Fact]
    public async Task A_run_recorded_as_completed_without_an_operation_never_borrows_another_runs_delivery()
    {
        using var host = StartHost(runSupervisor: true);
        var (delivered, _) = await DeliverAsync(host);
        var other = await LocalCommitLineage.SeedAsync(host, Scene, continueFrom: delivered);

        await ExecuteSqlAsync(
            $"UPDATE runs SET Lifecycle = 'Completed', Stage = 'Completed' WHERE Id = '{Upper(other.RunId)}'");

        var history = await HistoryAsync(host, delivered.ProjectId);
        var entry = EntryOf(history.Page, other.RunId);
        Assert.Equal("Completed", entry.GetProperty("lifecycle").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("receiptSource").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, EntryOf(history.Page, delivered.RunId).GetProperty("receiptSource").ValueKind);
    }

    public static TheoryData<string, string> IncoherentOperations() => new()
    {
        { "the operation belongs to another project", "UPDATE local_commit_operations SET \"ProjectId\" = '{someone}' WHERE \"RunId\" = '{run}'" },
        { "the operation is not completed", "UPDATE local_commit_operations SET \"Status\" = 'Prepared' WHERE \"RunId\" = '{run}'" },
        { "the operation status is unrecognized", "UPDATE local_commit_operations SET \"Status\" = 'Archived' WHERE \"RunId\" = '{run}'" },
        { "the commit is malformed", "UPDATE local_commit_operations SET \"CommitSha\" = 'not-a-sha' WHERE \"RunId\" = '{run}'" },
        { "the commit is uppercase", "UPDATE local_commit_operations SET \"CommitSha\" = upper(\"CommitSha\") WHERE \"RunId\" = '{run}'" },
        { "the checkpoint number is not positive", "UPDATE local_commit_operations SET \"CheckpointNumber\" = 0 WHERE \"RunId\" = '{run}'" },
        { "the checkpoint identity is empty", "UPDATE local_commit_operations SET \"GitCheckpointId\" = '00000000-0000-0000-0000-000000000000' WHERE \"RunId\" = '{run}'" },
        { "the completion time is missing", "UPDATE local_commit_operations SET \"CompletedAtUtc\" = NULL WHERE \"RunId\" = '{run}'" },
        { "the run is no longer recorded as completed", "UPDATE runs SET \"Lifecycle\" = 'Running' WHERE \"Id\" = '{run}'" },
        { "the run lifecycle is unrecognized", "UPDATE runs SET \"Lifecycle\" = 'Archived' WHERE \"Id\" = '{run}'" },
        { "the run stage is not completed", "UPDATE runs SET \"Stage\" = 'Execute' WHERE \"Id\" = '{run}'" },
        { "the run stage is unrecognized", "UPDATE runs SET \"Stage\" = 'Nowhere' WHERE \"Id\" = '{run}'" },
    };

    [Theory]
    [MemberData(nameof(IncoherentOperations))]
    public async Task A_source_that_cannot_be_coherently_identified_is_null_and_restoring_the_rows_returns_it(string name, string template)
    {
        using var host = StartHost(runSupervisor: true);
        var (delivered, _) = await DeliverAsync(host);
        var undamaged = EntryOf((await HistoryAsync(host, delivered.ProjectId)).Page, delivered.RunId).GetProperty("receiptSource").GetRawText();
        Assert.NotEqual("null", undamaged);

        var statement = template
            .Replace("{someone}", Upper(Guid.NewGuid()), StringComparison.Ordinal)
            .Replace("{run}", Upper(delivered.RunId), StringComparison.Ordinal);
        var table = template.StartsWith("UPDATE runs", StringComparison.Ordinal) ? "runs" : "local_commit_operations";

        await CorruptAsync([table], [statement], async () =>
        {
            var history = await HistoryAsync(host, delivered.ProjectId);
            Assert.True(HttpStatusCode.OK == history.Status, name);
            var entry = EntryOf(history.Page, delivered.RunId);
            Assert.True(entry.GetProperty("receiptSource").ValueKind == JsonValueKind.Null, name);
        });

        var restored = EntryOf((await HistoryAsync(host, delivered.ProjectId)).Page, delivered.RunId).GetProperty("receiptSource").GetRawText();
        Assert.Equal(undamaged, restored);
    }

    [Fact]
    public async Task Reading_history_and_receipts_writes_nothing_and_reaches_no_git_adapter_or_file()
    {
        LocalCommitLineageIds delivered;
        LocalCommitLineageIds later;
        using (var delivering = StartHost(runSupervisor: true))
        {
            (delivered, _) = await DeliverAsync(delivering);
            later = await LocalCommitLineage.SeedAsync(delivering, Scene, continueFrom: delivered);
        }

        var tripwire = new ExternalCallTripwire();
        using var reader = StartHost(
            runSupervisor: false,
            decorate: repository => tripwire.Wrap<ILocalCommitRepository>(repository),
            decoratePreparer: preparer => tripwire.Wrap<ILocalCommitPreparer>(preparer));
        tripwire.Arm();

        var databaseBefore = await DatabaseFingerprintAsync();
        var mainBefore = Scene.MainRepositoryFingerprint();
        var workspaceBefore = Scene.RunWorkspaceGit("status", "--porcelain=v1", "-z");
        var tipBefore = Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim();
        var storageBefore = Scene.StorageLeaves();
        var deliveredEventsBefore = await EventTypeRowsAsync(delivered.RunId);
        var laterEventsBefore = await EventTypeRowsAsync(later.RunId);
        var operationsBefore = await OperationCountAsync(reader);

        for (var read = 0; read < 3; read++)
        {
            Assert.Equal(HttpStatusCode.OK, (await HistoryAsync(reader, delivered.ProjectId)).Status);
            Assert.Equal(HttpStatusCode.OK, (await HistoryAsync(reader, delivered.ProjectId, "limit=1")).Status);
            Assert.Equal("Available", (await ReceiptAsync(reader, delivered.RunId)).State);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await HistoryAsync(reader, Guid.NewGuid())).Status);

        Assert.Empty(tripwire.Calls);
        Assert.Equal(databaseBefore, await DatabaseFingerprintAsync());
        Assert.Equal(deliveredEventsBefore, await EventTypeRowsAsync(delivered.RunId));
        Assert.Equal(laterEventsBefore, await EventTypeRowsAsync(later.RunId));
        Assert.Equal(operationsBefore, await OperationCountAsync(reader));
        Assert.Equal(mainBefore, Scene.MainRepositoryFingerprint());
        Assert.Equal(workspaceBefore, Scene.RunWorkspaceGit("status", "--porcelain=v1", "-z"));
        Assert.Equal(tipBefore, Scene.RunMainGit("rev-parse", "refs/heads/" + Scene.BranchName).Trim());
        Assert.Equal(storageBefore, Scene.StorageLeaves());
    }
}
