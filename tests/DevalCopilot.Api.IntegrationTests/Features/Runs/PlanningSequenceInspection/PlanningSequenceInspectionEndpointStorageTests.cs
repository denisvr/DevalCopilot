using System.Net;
using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequenceInspection;

/// <summary>Damaged and boundary storage through the real host (ADR-0068). Each test owns a fresh recorded five-step sequence and
/// database, because the damage it writes would break every later read of a shared one: a present but unreadable sequence is a 200
/// Unavailable (never a 404, 500 or NotRecorded), a recorded revision at the largest browser-exact integer is served as its canonical
/// string, and one above it (including the signed 64-bit maximum) is Unavailable and never rounded.</summary>
public sealed class PlanningSequenceInspectionEndpointStorageTests
{
    private static string Index(Guid run) => $"/api/runs/{run}/planning-sequence/inspection";

    private static string Step(Guid run, Guid step) => $"{Index(run)}/steps/{step}";

    private static async Task ExecuteAsync(PlanningSequenceInspectionApiFixture fixture, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={fixture.Harness.DatabasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Test-owned statement text; it holds no request or user input.
        command.CommandText = "PRAGMA ignore_check_constraints = ON; PRAGMA foreign_keys = OFF; DROP TRIGGER IF EXISTS trg_planning_sequence_immutable; "
            + "DROP TRIGGER IF EXISTS trg_planning_sequence_step_immutable; " + sql;
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(PlanningSequenceInspectionApiFixture fixture, string route)
    {
        using var client = fixture.Host.CreateClient();
        var response = await client.GetAsync(route);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response.StatusCode, document.RootElement.Clone());
    }

    [Fact]
    public async Task A_present_but_unreadable_sequence_is_unavailable_with_a_null_header_and_an_empty_list_never_absent_or_a_server_error()
    {
        var fixture = new PlanningSequenceInspectionApiFixture();
        await fixture.InitializeAsync();
        try
        {
            var stepId = fixture.Steps[1].Id;
            Assert.Equal("Recorded", (await GetAsync(fixture, Index(fixture.Scene.RunId))).Body.GetProperty("status").GetString());

            await ExecuteAsync(fixture, "UPDATE planning_sequence_steps SET Role = 'Bogus' WHERE Ordinal = 2; "
                + "UPDATE planning_sequence_steps SET PreparationReservedAtUtc = 'not-a-time' WHERE Ordinal = 3");

            var index = await GetAsync(fixture, Index(fixture.Scene.RunId));
            var step = await GetAsync(fixture, Step(fixture.Scene.RunId, stepId));

            Assert.Equal(HttpStatusCode.OK, index.Status);
            Assert.Equal("Unavailable", index.Body.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, index.Body.GetProperty("sequence").ValueKind);
            Assert.Equal(0, index.Body.GetProperty("steps").GetArrayLength());
            Assert.Equal(HttpStatusCode.OK, step.Status);
            Assert.Equal("Unavailable", step.Body.GetProperty("status").GetString());
            Assert.Equal(fixture.Sequence.Id, step.Body.GetProperty("sequenceId").GetGuid());
            Assert.Equal(JsonValueKind.Null, step.Body.GetProperty("step").ValueKind);
            Assert.Equal(JsonValueKind.Null, step.Body.GetProperty("agent").ValueKind);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task Unsupported_attempt_number_storage_is_a_200_unavailable_with_null_payloads_never_a_server_error_and_int_max_stays_recorded()
    {
        var fixture = new PlanningSequenceInspectionApiFixture();
        await fixture.InitializeAsync();
        try
        {
            var stepId = fixture.Steps[1].Id;
            var attemptId = fixture.Steps[1].AttemptId!.Value.ToString().ToUpperInvariant();
            foreach (var unsupported in new[] { "1.75", "2147483648", "4294967297", "'7tail'", "X'31'" })
            {
                await ExecuteAsync(fixture, $"UPDATE attempts SET AttemptNumber = {unsupported} WHERE Id = '{attemptId}'");

                var step = await GetAsync(fixture, Step(fixture.Scene.RunId, stepId));
                var index = await GetAsync(fixture, Index(fixture.Scene.RunId));

                Assert.True(HttpStatusCode.OK == step.Status, $"A stored attempt number of {unsupported} answered {step.Status}.");
                Assert.Equal("Unavailable", step.Body.GetProperty("status").GetString());
                Assert.Equal(fixture.Sequence.Id, step.Body.GetProperty("sequenceId").GetGuid());
                Assert.Equal(JsonValueKind.Null, step.Body.GetProperty("step").ValueKind);
                Assert.Equal(JsonValueKind.Null, step.Body.GetProperty("agent").ValueKind);
                Assert.Equal(HttpStatusCode.OK, index.Status);
                Assert.Equal("Recorded", index.Body.GetProperty("status").GetString());
            }

            await ExecuteAsync(fixture, $"UPDATE attempts SET AttemptNumber = 2147483647 WHERE Id = '{attemptId}'");
            var maximum = await GetAsync(fixture, Step(fixture.Scene.RunId, stepId));

            Assert.Equal("Recorded", maximum.Body.GetProperty("status").GetString());
            Assert.Equal(int.MaxValue, maximum.Body.GetProperty("agent").GetProperty("attemptNumber").GetInt32());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_reserved_revoked_or_unresolved_last_step_is_a_200_no_linked_attempt_with_null_payloads_beside_a_recorded_index()
    {
        var fixture = new PlanningSequenceInspectionApiFixture();
        await fixture.InitializeAsync();
        try
        {
            var stepId = fixture.Steps[4].Id;
            var shapes = new (string Sql, string State)[]
            {
                ("State = 'Reserved', AttemptId = NULL, ReasonCode = NULL", "Reserved"),
                ("State = 'Revoked', AttemptId = NULL, ReasonCode = 'stopped_before_claim'", "Revoked"),
                ("State = 'Unresolved', AttemptId = NULL, ReasonCode = 'preparation_unresolved'", "Unresolved"),
            };
            foreach (var (sql, state) in shapes)
            {
                await ExecuteAsync(fixture, $"UPDATE planning_sequence_steps SET {sql} WHERE Ordinal = 4");

                var step = await GetAsync(fixture, Step(fixture.Scene.RunId, stepId));
                var index = await GetAsync(fixture, Index(fixture.Scene.RunId));

                Assert.Equal(HttpStatusCode.OK, step.Status);
                Assert.Equal("NoLinkedAttempt", step.Body.GetProperty("status").GetString());
                Assert.Equal(stepId, step.Body.GetProperty("stepId").GetGuid());
                Assert.Equal(fixture.Sequence.Id, step.Body.GetProperty("sequenceId").GetGuid());
                Assert.Equal(JsonValueKind.Null, step.Body.GetProperty("step").ValueKind);
                Assert.Equal(JsonValueKind.Null, step.Body.GetProperty("agent").ValueKind);
                Assert.Equal("Recorded", index.Body.GetProperty("status").GetString());
                var member = index.Body.GetProperty("steps")[4];
                Assert.Equal(state, member.GetProperty("recordedState").GetString());
                Assert.Equal(JsonValueKind.Null, member.GetProperty("attemptId").ValueKind);
            }
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_revision_at_the_largest_browser_exact_integer_is_a_canonical_string_and_anything_above_it_is_unavailable()
    {
        var fixture = new PlanningSequenceInspectionApiFixture();
        await fixture.InitializeAsync();
        try
        {
            await ExecuteAsync(fixture, "UPDATE planning_sequences SET Revision = 9007199254740991");
            var exact = await GetAsync(fixture, Index(fixture.Scene.RunId));

            Assert.Equal("Recorded", exact.Body.GetProperty("status").GetString());
            Assert.Equal("9007199254740991", exact.Body.GetProperty("sequence").GetProperty("revision").GetString());

            foreach (var unsupported in new[] { "9007199254740992", "9223372036854775807", "-1", "0", "1.5", "'seven'" })
            {
                await ExecuteAsync(fixture, $"UPDATE planning_sequences SET Revision = {unsupported}");

                var response = await GetAsync(fixture, Index(fixture.Scene.RunId));

                Assert.Equal(HttpStatusCode.OK, response.Status);
                Assert.True(
                    "Unavailable" == response.Body.GetProperty("status").GetString(),
                    $"A stored revision of {unsupported} must be unavailable.");
                Assert.DoesNotContain("9007199254740992", response.Body.GetRawText(), StringComparison.Ordinal);
            }

            await ExecuteAsync(fixture, "UPDATE planning_sequence_steps SET Revision = 9007199254740991 WHERE Ordinal = 4; "
                + "UPDATE planning_sequences SET Revision = 9");
            var step = await GetAsync(fixture, Index(fixture.Scene.RunId));
            Assert.Equal("9007199254740991", step.Body.GetProperty("steps")[4].GetProperty("revision").GetString());
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
