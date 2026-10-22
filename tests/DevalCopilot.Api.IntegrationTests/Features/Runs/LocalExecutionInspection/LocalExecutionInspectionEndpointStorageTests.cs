using System.Net;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Application.Features.Runs.Commands.ObserveLocalExecutionSequence;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionInspection;

/// <summary>The non-Recorded wire states of ADR-0061 over a REAL recorded cycle whose storage this class changes (so it owns its host and
/// database): a coherent no-link step, a damaged structural set, an absent stream row and the artifact store's own Missing and
/// IntegrityMismatch refusals. Every refusal is an explicit 200 with null or empty members, the requested offset unchanged and no
/// artifact read where the contract says none.</summary>
public sealed class LocalExecutionInspectionEndpointStorageTests : IAsyncLifetime
{
    private LocalExecutionSequenceHostedHarness harness = null!;
    private LocalExecutionSequenceLineage lineage = null!;
    private LocalExecutionInspectionHost host = null!;
    private LocalExecutionSequence sequence = null!;
    private IReadOnlyList<LocalExecutionSequenceStep> steps = [];

    public async Task InitializeAsync()
    {
        harness = new LocalExecutionSequenceHostedHarness();
        lineage = await harness.SeedLineageAsync(recipeCount: 1);
        harness.Process.Verification = _ => LocalExecutionSequenceVerificationBehavior.Fails;
        var armed = await harness.ArmAsync(lineage);
        await harness.StartHostsAsync();
        sequence = await harness.WaitForEndAsync(armed.SequenceId);
        await harness.StopHostsAsync();
        steps = await harness.StepsAsync(sequence.Id);
        host = new LocalExecutionInspectionHost(harness.DatabasePath, harness.ArtifactStore);
    }

    public async Task DisposeAsync()
    {
        host.Dispose();
        await harness.DisposeAsync();
    }

    private LocalExecutionSequenceStep Verification => steps.First(step => step.Kind == LocalExecutionStepKind.Verification);

    private static string Index(Guid run) => $"/api/runs/{run}/local-execution-sequence/inspection";

    private static string Step(Guid run, Guid step) => $"{Index(run)}/steps/{step}";

    private static string Window(Guid run, Guid step, string stream, string? query = null) =>
        $"{Step(run, step)}/artifact-window/{stream}" + (query is null ? string.Empty : "?" + query);

    private async Task<JsonElement> GetOkAsync(string route)
    {
        using var client = host.CreateClient();
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task CorruptAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={harness.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-owned statement text without any caller-supplied value.
        command.CommandText = "PRAGMA ignore_check_constraints = ON; PRAGMA foreign_keys = OFF; " + sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_step_revoked_by_a_restart_is_no_operation_with_null_payloads_and_the_index_lists_it_without_a_link()
    {
        // The API host closes every live sequence at startup (a restart never resumes): the reserved step it finds is revoked with the
        // fixed host_restarted reason, a real production no-link shape.
        await using var other = new LocalExecutionSequenceHostedHarness();
        var seeded = await other.SeedLineageAsync(recipeCount: 1);
        var armed = await other.ArmAsync(seeded);
        var observed = await other.SendAsync(new ObserveLocalExecutionSequenceCommand(armed.SequenceId));
        Assert.True(observed.IsSuccess, observed.IsFailure ? observed.Errors[0].Code : null);
        var reserved = (await other.StepsAsync(armed.SequenceId)).Single();
        using var otherHost = new LocalExecutionInspectionHost(other.DatabasePath, other.ArtifactStore);
        using var client = otherHost.CreateClient();

        var step = JsonDocument.Parse(await client.GetStringAsync(Step(seeded.RunId, reserved.Id))).RootElement;
        var index = JsonDocument.Parse(await client.GetStringAsync(Index(seeded.RunId))).RootElement;

        Assert.Equal("NoOperation", step.GetProperty("status").GetString());
        Assert.Equal(reserved.Id, step.GetProperty("stepId").GetGuid());
        Assert.Equal(armed.SequenceId, step.GetProperty("sequenceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, step.GetProperty("step").ValueKind);
        Assert.Equal(JsonValueKind.Null, step.GetProperty("agent").ValueKind);
        Assert.Equal(JsonValueKind.Null, step.GetProperty("verification").ValueKind);
        Assert.Equal("Recorded", index.GetProperty("status").GetString());
        Assert.Equal("Interrupted", index.GetProperty("sequence").GetProperty("recordedState").GetString());
        var member = Assert.Single(index.GetProperty("steps").EnumerateArray());
        Assert.Equal("Revoked", member.GetProperty("recordedState").GetString());
        Assert.Equal("host_restarted", member.GetProperty("reasonCode").GetString());
        Assert.Equal(JsonValueKind.Null, member.GetProperty("operationId").ValueKind);
    }

    [Fact]
    public async Task A_damaged_structural_set_is_unavailable_on_the_wire_with_no_partial_list_and_a_window_reads_nothing()
    {
        await CorruptAsync("DROP TRIGGER IF EXISTS trg_local_execution_step_immutable; "
            + "UPDATE local_execution_sequence_steps SET SourceFingerprintSha256 = upper(SourceFingerprintSha256) WHERE Ordinal = 1");
        var before = host.Store.Calls;

        var index = await GetOkAsync(Index(lineage.RunId));
        var step = await GetOkAsync(Step(lineage.RunId, steps[0].Id));
        var window = await GetOkAsync(Window(lineage.RunId, Verification.Id, "stdout", "fromOffset=4"));

        Assert.Equal("Unavailable", index.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, index.GetProperty("sequence").ValueKind);
        Assert.Equal(0, index.GetProperty("steps").GetArrayLength());
        Assert.Equal("Unavailable", step.GetProperty("status").GetString());
        Assert.Equal(sequence.Id, step.GetProperty("sequenceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, step.GetProperty("step").ValueKind);
        Assert.Equal("Unavailable", window.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, window.GetProperty("executionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, window.GetProperty("artifact").ValueKind);
        Assert.Equal(string.Empty, window.GetProperty("text").GetString());
        Assert.Equal(4, window.GetProperty("nextOffset").GetInt64());
        Assert.Equal(0, window.GetProperty("totalLength").GetInt64());
        Assert.Equal(before, host.Store.Calls);
    }

    [Fact]
    public async Task An_absent_stream_row_is_a_not_recorded_descriptor_and_a_not_recorded_window_that_names_the_proved_execution()
    {
        await using (var db = harness.OpenContext())
        {
            await db.VerificationOutputArtifacts.Where(output => output.VerificationExecutionId == Verification.OperationId
                && output.Purpose == VerificationOutputPurpose.StandardError).ExecuteDeleteAsync();
        }

        var before = host.Store.Calls;
        var step = await GetOkAsync(Step(lineage.RunId, Verification.Id));
        var window = await GetOkAsync(Window(lineage.RunId, Verification.Id, "stderr", "fromOffset=3"));

        var verification = step.GetProperty("verification");
        Assert.Equal("NotRecorded", verification.GetProperty("standardError").GetProperty("status").GetString());
        Assert.Equal("Recorded", verification.GetProperty("standardOutput").GetProperty("status").GetString());
        Assert.Equal("NotRecorded", window.GetProperty("status").GetString());
        Assert.Equal(Verification.OperationId, window.GetProperty("executionId").GetGuid());
        Assert.Equal(3, window.GetProperty("nextOffset").GetInt64());
        Assert.Equal(before, host.Store.Calls);
    }

    [Fact]
    public async Task A_missing_and_a_tampered_sealed_file_are_the_stores_own_distinct_refusals_with_no_text_length_or_diagnostic()
    {
        var outputs = await harness.InDbAsync(db => db.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == Verification.OperationId).ToListAsync());
        var stdout = outputs.Single(output => output.Purpose == VerificationOutputPurpose.StandardOutput).RelativeStoragePath;
        var stderr = outputs.Single(output => output.Purpose == VerificationOutputPurpose.StandardError).RelativeStoragePath;
        var root = harness.ArtifactRoot;
        File.Delete(Path.Combine(root, stdout));
        await File.WriteAllTextAsync(Path.Combine(root, stderr), "tampered with a different length SECRET-SENTINEL");

        var missing = await GetOkAsync(Window(lineage.RunId, Verification.Id, "stdout", "fromOffset=2"));
        var tampered = await GetOkAsync(Window(lineage.RunId, Verification.Id, "stderr", "fromOffset=2"));

        foreach (var (window, status) in new[] { (missing, "Missing"), (tampered, "IntegrityMismatch") })
        {
            Assert.Equal(status, window.GetProperty("status").GetString());
            Assert.Equal(Verification.OperationId, window.GetProperty("executionId").GetGuid());
            Assert.Equal(JsonValueKind.Null, window.GetProperty("artifact").ValueKind);
            Assert.Equal(string.Empty, window.GetProperty("text").GetString());
            Assert.Equal(2, window.GetProperty("nextOffset").GetInt64());
            Assert.Equal(0, window.GetProperty("totalLength").GetInt64());
            Assert.DoesNotContain("SECRET-SENTINEL", window.GetRawText(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public async Task A_stored_truncation_that_is_not_exactly_zero_or_one_is_unavailable_on_the_wire_for_that_stream_and_reads_nothing(string stored)
    {
        await CorruptAsync($"UPDATE verification_output_artifacts SET Truncated = {stored} "
            + $"WHERE VerificationExecutionId = '{Verification.OperationId!.Value.ToString().ToUpperInvariant()}' AND Purpose = 'StandardOutput'");
        var before = host.Store.Calls;

        var step = await GetOkAsync(Step(lineage.RunId, Verification.Id));
        var window = await GetOkAsync(Window(lineage.RunId, Verification.Id, "stdout", "fromOffset=2"));
        var sibling = await GetOkAsync(Window(lineage.RunId, Verification.Id, "stderr"));

        var verification = step.GetProperty("verification");
        Assert.Equal("Unavailable", verification.GetProperty("standardOutput").GetProperty("status").GetString());
        Assert.Equal("Recorded", verification.GetProperty("standardError").GetProperty("status").GetString());
        Assert.Equal("Unavailable", window.GetProperty("status").GetString());
        Assert.Equal(Verification.OperationId, window.GetProperty("executionId").GetGuid());
        Assert.Equal(2, window.GetProperty("nextOffset").GetInt64());
        Assert.Equal("Ok", sibling.GetProperty("status").GetString());
        Assert.Equal(before + 1, host.Store.Calls);
    }

    [Fact]
    public async Task A_stream_row_with_a_rooted_path_is_unavailable_and_never_reaches_the_store()
    {
        await CorruptAsync("UPDATE verification_output_artifacts SET RelativeStoragePath = 'C:\\outside\\file' "
            + $"WHERE VerificationExecutionId = '{Verification.OperationId!.Value.ToString().ToUpperInvariant()}' AND Purpose = 'StandardOutput'");
        var before = host.Store.Calls;

        var window = await GetOkAsync(Window(lineage.RunId, Verification.Id, "stdout"));

        Assert.Equal("Unavailable", window.GetProperty("status").GetString());
        Assert.Equal(Verification.OperationId, window.GetProperty("executionId").GetGuid());
        Assert.Equal(before, host.Store.Calls);
    }
}
