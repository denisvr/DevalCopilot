using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0033: the protected, bodyless, read-only history of one project's runs against the real host and file-backed SQLite. Rows are
/// seeded through the Domain factories (a run is not approval authority); only the deliberately unrecognized stored values are
/// written as SQL, exactly as the existing intake and summary tests do.
/// </summary>
public sealed class ProjectRunHistoryEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private HttpClient AuthenticatedClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string Route(Guid projectId, string? query = null) =>
        $"/api/projects/{projectId}/run-history{(query is null ? string.Empty : "?" + query)}";

    private async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private Task<Guid> RegisterProjectAsync() => WithDbAsync(async db =>
    {
        var project = Project.Register(Guid.NewGuid(), "History project", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    });

    /// <summary>Records <paramref name="count"/> runs with consecutive execution numbers through the Domain factory and returns their ids.</summary>
    private Task<List<Guid>> SeedRunsAsync(Guid projectId, int count, Action<Run, int>? shape = null) => WithDbAsync(async db =>
    {
        var project = await db.Projects.SingleAsync(candidate => candidate.Id == projectId);
        var ids = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            var number = project.ReserveExecutionNumber();
            var run = Run.RecordClassifiedIntent(Guid.NewGuid(), projectId, number, RunExecutionMode.ManualAgent, $"Objective {number}", DateTimeOffset.UtcNow);
            shape?.Invoke(run, number);
            db.Runs.Add(run);
            ids.Add(run.Id);
        }

        await db.SaveChangesAsync();
        return ids;
    });

    private async Task<(HttpStatusCode Status, JsonElement Body, string Text)> ReadAsync(Guid projectId, string? query = null)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(Route(projectId, query));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    private static int[] Numbers(JsonElement page) =>
        page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("executionNumber").GetInt32()).ToArray();

    [Fact]
    public async Task The_route_requires_authentication_and_a_wrong_credential_is_refused()
    {
        var projectId = await RegisterProjectAsync();

        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route(projectId))).StatusCode);

        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-launch-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(Route(projectId))).StatusCode);

        using var client = AuthenticatedClient();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(Route(projectId), null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync(Route(projectId))).StatusCode);
    }

    [Fact]
    public void Every_route_that_mentions_run_history_is_one_authorized_get_with_no_anonymous_metadata()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains("run-history", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        var endpoint = Assert.Single(endpoints);
        Assert.Equal("api/projects/{projectId:guid}/run-history", endpoint.RoutePattern.RawText);
        Assert.Equal(["GET"], endpoint.Metadata.GetOrderedMetadata<HttpMethodMetadata>().Single().HttpMethods);
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public async Task An_unknown_project_is_a_safe_not_found_and_an_existing_project_without_runs_is_an_empty_page()
    {
        var unknown = await ReadAsync(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.Contains("projects.not_found", unknown.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", unknown.Text, StringComparison.Ordinal);

        var projectId = await RegisterProjectAsync();
        var empty = await ReadAsync(projectId);
        Assert.Equal(HttpStatusCode.OK, empty.Status);
        Assert.Equal(projectId, empty.Body.GetProperty("projectId").GetGuid());
        Assert.Empty(empty.Body.GetProperty("entries").EnumerateArray());
        Assert.False(empty.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, empty.Body.GetProperty("nextBeforeExecutionNumber").ValueKind);
    }

    [Fact]
    public async Task Pages_descend_by_execution_number_with_an_exclusive_cursor_and_never_leak_another_project()
    {
        var projectId = await RegisterProjectAsync();
        var otherId = await RegisterProjectAsync();
        // Interleave a second project's runs so a missing project scope would be visible in the numbers and identities.
        await SeedRunsAsync(otherId, 7);
        var runIds = await SeedRunsAsync(projectId, 25);
        await SeedRunsAsync(otherId, 7);

        var first = await ReadAsync(projectId);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(Enumerable.Range(16, 10).Reverse(), Numbers(first.Body).Select(number => number));
        Assert.Equal(25, Numbers(first.Body)[0]);
        Assert.True(first.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(16, first.Body.GetProperty("nextBeforeExecutionNumber").GetInt32());

        var second = await ReadAsync(projectId, "beforeExecutionNumber=16");
        Assert.Equal(Enumerable.Range(6, 10).Reverse(), Numbers(second.Body));
        Assert.True(second.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(6, second.Body.GetProperty("nextBeforeExecutionNumber").GetInt32());

        var third = await ReadAsync(projectId, "beforeExecutionNumber=6");
        Assert.Equal(Enumerable.Range(1, 5).Reverse(), Numbers(third.Body));
        Assert.False(third.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, third.Body.GetProperty("nextBeforeExecutionNumber").ValueKind);

        foreach (var page in new[] { first, second, third })
        {
            Assert.Equal(projectId, page.Body.GetProperty("projectId").GetGuid());
            foreach (var entry in page.Body.GetProperty("entries").EnumerateArray())
            {
                Assert.Equal(projectId, entry.GetProperty("projectId").GetGuid());
                Assert.Contains(entry.GetProperty("runId").GetGuid(), runIds);
            }
        }
    }

    [Fact]
    public async Task A_page_that_exactly_fills_the_limit_reports_no_more_and_the_boundary_limits_are_accepted()
    {
        var projectId = await RegisterProjectAsync();
        await SeedRunsAsync(projectId, 20);

        var exact = await ReadAsync(projectId, "limit=20");
        Assert.Equal(20, Numbers(exact.Body).Length);
        Assert.False(exact.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, exact.Body.GetProperty("nextBeforeExecutionNumber").ValueKind);

        var one = await ReadAsync(projectId, "limit=1");
        Assert.Equal([20], Numbers(one.Body));
        Assert.True(one.Body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(20, one.Body.GetProperty("nextBeforeExecutionNumber").GetInt32());

        var beforeOldest = await ReadAsync(projectId, "beforeExecutionNumber=1");
        Assert.Empty(beforeOldest.Body.GetProperty("entries").EnumerateArray());
        Assert.False(beforeOldest.Body.GetProperty("hasMore").GetBoolean());

        var beyond = await ReadAsync(projectId, "beforeExecutionNumber=2147483647&limit=20");
        Assert.Equal(20, Numbers(beyond.Body).Length);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=-1")]
    [InlineData("limit=21")]
    [InlineData("limit=1000")]
    [InlineData("limit=abc")]
    [InlineData("limit=99999999999")]
    [InlineData("limit=1.5")]
    [InlineData("beforeExecutionNumber=0")]
    [InlineData("beforeExecutionNumber=-5")]
    [InlineData("beforeExecutionNumber=abc")]
    [InlineData("beforeExecutionNumber=99999999999")]
    [InlineData("beforeExecutionNumber=2.5")]
    [InlineData("beforeExecutionNumber=3&limit=0")]
    public async Task An_invalid_limit_or_cursor_is_a_safe_bad_request_and_is_never_clamped_or_answered_with_a_page(string query)
    {
        var projectId = await RegisterProjectAsync();
        await SeedRunsAsync(projectId, 3);

        var result = await ReadAsync(projectId, query);

        // The shared problem body names the request path the caller itself used and never repeats a rejected value (the full error
        // contract is ProjectRunHistoryErrorContractTests); no state, stack or recorded fact appears, and no page is returned.
        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.DoesNotContain("Exception", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("entries", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Objective", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("executionNumber", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_newer_run_inserted_between_pages_neither_repeats_nor_skips_an_older_run()
    {
        var projectId = await RegisterProjectAsync();
        await SeedRunsAsync(projectId, 25);

        var first = await ReadAsync(projectId);
        var cursor = first.Body.GetProperty("nextBeforeExecutionNumber").GetInt32();
        await SeedRunsAsync(projectId, 1);

        var second = await ReadAsync(projectId, $"beforeExecutionNumber={cursor}");
        Assert.Equal(Enumerable.Range(6, 10).Reverse(), Numbers(second.Body));
        Assert.DoesNotContain(26, Numbers(second.Body));

        var fresh = await ReadAsync(projectId);
        Assert.Equal(26, Numbers(fresh.Body)[0]);
        Assert.Equal(25, Numbers(fresh.Body)[1]);
    }

    [Fact]
    public async Task Every_recorded_lifecycle_stage_and_mode_is_disclosed_with_its_times_and_no_source_without_an_operation()
    {
        var projectId = await RegisterProjectAsync();
        var runIds = await WithDbAsync(async db =>
        {
            var project = await db.Projects.SingleAsync(candidate => candidate.Id == projectId);
            var now = DateTimeOffset.UtcNow;
            Run Make(RunExecutionMode mode, string objective) =>
                Run.RecordClassifiedIntent(Guid.NewGuid(), projectId, project.ReserveExecutionNumber(), mode, objective, now);

            var created = Make(RunExecutionMode.ManualAgent, "Created manual");
            var running = Make(RunExecutionMode.Simulated, "Running simulated");
            running.Claim(now.AddMinutes(1));
            var completed = Make(RunExecutionMode.Simulated, "Completed simulated");
            completed.Claim(now.AddMinutes(1));
            completed.Complete(now.AddMinutes(2));
            var failed = Make(RunExecutionMode.Simulated, "Failed simulated");
            failed.Claim(now.AddMinutes(1));
            failed.Fail(now.AddMinutes(3));
            var interrupted = Make(RunExecutionMode.Simulated, "Interrupted simulated");
            interrupted.Claim(now.AddMinutes(1));
            interrupted.MarkInterrupted(now.AddMinutes(4));
            var abandoned = Make(RunExecutionMode.ManualAgent, "Abandoned manual");
            abandoned.Abandon("No longer needed", now.AddMinutes(5));
            var legacy = Run.RecordIntent(Guid.NewGuid(), projectId, project.ReserveExecutionNumber(), "Legacy", now);

            var all = new[] { created, running, completed, failed, interrupted, abandoned, legacy };
            db.Runs.AddRange(all);
            await db.SaveChangesAsync();
            return all.Select(run => run.Id).ToArray();
        });

        var page = await ReadAsync(projectId);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var entries = page.Body.GetProperty("entries").EnumerateArray().ToList();
        var byId = entries.ToDictionary(entry => entry.GetProperty("runId").GetGuid());

        void Expect(Guid runId, string lifecycle, string stage, string mode, string objective)
        {
            var entry = byId[runId];
            Assert.Equal(lifecycle, entry.GetProperty("lifecycle").GetString());
            Assert.Equal(stage, entry.GetProperty("stage").GetString());
            Assert.Equal(mode, entry.GetProperty("executionMode").GetString());
            Assert.Equal(objective, entry.GetProperty("objective").GetString());
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("receiptSource").ValueKind);
            Assert.True(entry.GetProperty("createdAtUtc").GetDateTimeOffset() <= entry.GetProperty("lastAdvancedAtUtc").GetDateTimeOffset());
        }

        Expect(runIds[0], "Created", "Intake", "ManualAgent", "Created manual");
        Expect(runIds[1], "Running", "Intake", "Simulated", "Running simulated");
        Expect(runIds[2], "Completed", "Completed", "Simulated", "Completed simulated");
        Expect(runIds[3], "Failed", "Intake", "Simulated", "Failed simulated");
        Expect(runIds[4], "Interrupted", "Intake", "Simulated", "Interrupted simulated");
        Expect(runIds[5], "Abandoned", "Intake", "ManualAgent", "Abandoned manual");
        Expect(runIds[6], "Created", "Intake", "Legacy", "Legacy");

        var stored = await WithDbAsync(async db =>
            await db.Runs.AsNoTracking().Where(run => run.ProjectId == projectId).ToDictionaryAsync(run => run.Id));
        foreach (var (runId, entry) in byId)
        {
            Assert.Equal(stored[runId].CreatedAtUtc, entry.GetProperty("createdAtUtc").GetDateTimeOffset());
            Assert.Equal(stored[runId].LastAdvancedAtUtc, entry.GetProperty("lastAdvancedAtUtc").GetDateTimeOffset());
        }
    }

    public static TheoryData<string> MalformedStoredModes() =>
        ["9", "2.5", "1.0000001", "4294967296", "9223372036854775807", "-1", "'two'", "''", "X'02'", "X''"];

    [Theory]
    [MemberData(nameof(MalformedStoredModes))]
    public async Task An_unrecognized_stored_mode_keeps_its_row_and_never_fails_or_promotes_a_neighbor(string literal)
    {
        var projectId = await RegisterProjectAsync();
        var runIds = await SeedRunsAsync(projectId, 3);
#pragma warning disable EF1003
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE runs SET ExecutionMode = " + literal + " WHERE Id = {0}", runIds[1]));
#pragma warning restore EF1003

        var page = await ReadAsync(projectId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal([3, 2, 1], Numbers(page.Body));
        var modes = page.Body.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("executionMode").GetString()!).ToArray();
        Assert.Equal(["ManualAgent", "Unrecognized", "ManualAgent"], modes);
        if (literal == "'two'")
        {
            Assert.DoesNotContain("two", page.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_unrecognized_stored_lifecycle_or_stage_keeps_its_row_discloses_only_unrecognized_and_never_fails_neighbors()
    {
        var projectId = await RegisterProjectAsync();
        var runIds = await SeedRunsAsync(projectId, 4);
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Lifecycle = 'Archived' WHERE Id = {runIds[1]}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Stage = 'Nowhere' WHERE Id = {runIds[2]}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Lifecycle = '', Stage = 'completed' WHERE Id = {runIds[3]}");
            return 0;
        });

        var page = await ReadAsync(projectId);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal([4, 3, 2, 1], Numbers(page.Body));
        var entries = page.Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["Unrecognized", "Created", "Unrecognized", "Created"], entries.Select(e => e.GetProperty("lifecycle").GetString()!));
        Assert.Equal(["Unrecognized", "Unrecognized", "Intake", "Intake"], entries.Select(e => e.GetProperty("stage").GetString()!));
        Assert.All(entries, entry => Assert.Equal("ManualAgent", entry.GetProperty("executionMode").GetString()));
        Assert.DoesNotContain("Archived", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nowhere", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reading_history_writes_nothing()
    {
        var projectId = await RegisterProjectAsync();
        await SeedRunsAsync(projectId, 12);
        var before = await WithDbAsync(async db => (
            await db.Runs.CountAsync(), await db.Events.CountAsync(), await db.Attempts.CountAsync(), await db.LocalCommitOperations.CountAsync(),
            (await db.Projects.SingleAsync(project => project.Id == projectId)).NextExecutionNumber));

        for (var read = 0; read < 3; read++)
        {
            Assert.Equal(HttpStatusCode.OK, (await ReadAsync(projectId)).Status);
            Assert.Equal(HttpStatusCode.OK, (await ReadAsync(projectId, "beforeExecutionNumber=3")).Status);
        }

        var after = await WithDbAsync(async db => (
            await db.Runs.CountAsync(), await db.Events.CountAsync(), await db.Attempts.CountAsync(), await db.LocalCommitOperations.CountAsync(),
            (await db.Projects.SingleAsync(project => project.Id == projectId)).NextExecutionNumber));
        Assert.Equal(before, after);
    }
}
