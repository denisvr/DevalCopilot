using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;
using DevalCopilot.Api.Features.Runs.AbandonManualRun;
using DevalCopilot.Api.Features.Runs.CreateManualRun;
using DevalCopilot.Api.Features.Runs.GetManualRunAbandonment;
using DevalCopilot.Api.Features.Runs.GetRunCockpit;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>ADR-0031 through the real protected MVC host: authentication, strict validation, the 8 KiB body bound on real Kestrel,
/// the closure and its retained history, replay and conflict, and the read-only status. Nothing here runs a provider or a process.</summary>
public sealed class AbandonManualRunEndpointTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private const string Reason = "The objective was replaced by a smaller change.";

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    private async Task<(Guid ProjectId, Guid RunId)> SeedManualRunAsync(bool running = false, RunExecutionMode mode = RunExecutionMode.ManualAgent)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        return await WithDbAsync(async db =>
        {
            var project = Project.Register(Guid.NewGuid(), "Abandonment", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = mode == RunExecutionMode.Legacy
                ? Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Legacy objective", now)
                : Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), mode, "Plan the increment", now);
            if (running)
            {
                run.Claim(now.AddSeconds(5));
            }

            db.Projects.Add(project);
            db.Runs.Add(run);
            db.Events.Add(RunEvent.Record(
                Guid.NewGuid(), run.Id, null, RunEventType.RunStarted, ParticipantIdentity.ForOrchestrator(), "{\"objective\":\"x\"}", now));
            await db.SaveChangesAsync();
            return (project.Id, run.Id);
        });
    }

    private static string Route(Guid runId) => $"/api/runs/{runId}/abandon";

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors")[0].GetProperty("code").GetString()!;
    }

    // ---- authentication ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Both_operations_require_authentication_and_the_unauthenticated_post_changes_nothing()
    {
        var (_, runId) = await SeedManualRunAsync();
        using var anonymous = factory.CreateClient();

        var post = await anonymous.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = Reason });
        var get = await anonymous.GetAsync($"/api/runs/{runId}/abandonment");

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Equal(RunLifecycle.Created, (await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId))).Lifecycle);
    }

    // ---- the closure -----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_abandonment_is_recorded_and_visible_everywhere_a_reader_looks(bool running)
    {
        using var client = Client();
        var (projectId, runId) = await SeedManualRunAsync(running);

        var response = await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = "  " + Reason + "\r\n" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AbandonManualRunResponse>())!;
        Assert.Equal(runId, body.RunId);
        Assert.Equal(1, body.ExecutionNumber);
        Assert.Equal(Reason, body.Reason);

        var cockpit = (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit"))!;
        Assert.Equal("Abandoned", cockpit.Lifecycle);
        Assert.Equal("Intake", cockpit.Stage);
        Assert.Equal("ManualAgent", cockpit.ExecutionMode);
        Assert.Equal("None", cockpit.ActiveParticipant.Kind);

        var status = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{runId}/abandonment"))!;
        Assert.False(status.Eligible);
        Assert.Equal("run_abandonment.already_abandoned", status.RefusalCode);
        Assert.Equal(Reason, status.Abandonment!.Reason);
        Assert.Equal(body.AbandonedAtUtc, status.Abandonment.AbandonedAtUtc);

        var summaries = (await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries"))!;
        var summary = summaries.Single(candidate => candidate.ProjectId == projectId);
        Assert.Equal("Abandoned", summary.Lifecycle);
        Assert.True(summary.CanCreateRun);

        var events = (await client.GetFromJsonAsync<List<JsonElement>>($"/api/runs/{runId}/events?after=0"))!;
        var abandoned = Assert.Single(events, candidate => candidate.GetProperty("eventType").GetString() == "run.abandoned");
        Assert.Equal("Human", abandoned.GetProperty("actor").GetProperty("kind").GetString());
        Assert.Contains(Reason, abandoned.GetProperty("payloadJson").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_objective_can_then_be_recorded_through_the_ordinary_creation_route_as_a_distinct_run()
    {
        using var client = Client();
        var (projectId, runId) = await SeedManualRunAsync(running: true);
        (await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = Reason })).EnsureSuccessStatusCode();

        var created = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(projectId, "A different objective"));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var next = (await created.Content.ReadFromJsonAsync<CreateManualRunResponse>())!;
        Assert.NotEqual(runId, next.RunId);
        Assert.Equal(2, next.ExecutionNumber);
        var oldCockpit = (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit"))!;
        Assert.Equal("Abandoned", oldCockpit.Lifecycle);
    }

    // ---- replay and conflict ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_same_normalized_reason_returns_the_original_result_and_a_different_one_conflicts()
    {
        using var client = Client();
        var (_, runId) = await SeedManualRunAsync();
        var first = (await (await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = Reason }))
            .Content.ReadFromJsonAsync<AbandonManualRunResponse>())!;

        var replay = await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = Reason + "\r\n" });
        var different = await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = "Another reason" });

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first, await replay.Content.ReadFromJsonAsync<AbandonManualRunResponse>());
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("run_abandonment.reason_conflict", await ErrorCodeAsync(different));
        Assert.Equal(1, await WithDbAsync(db => db.Events.CountAsync(e => e.RunId == runId && e.EventType == RunEventType.RunAbandoned)));
    }

    // ---- validation and body bound ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("""{"reason":""}""")]
    [InlineData("""{"reason":"   "}""")]
    [InlineData("""{"reason":"tab\tseparated"}""")]
    [InlineData("""{"reason":"lone\rcarriage"}""")]
    [InlineData("""{"reason":null}""")]
    [InlineData("""{}""")]
    [InlineData("""{"reason":42}""")]
    public async Task An_invalid_or_missing_reason_is_a_bad_request_that_writes_nothing(string body)
    {
        using var client = Client();
        var (_, runId) = await SeedManualRunAsync();

        var response = await client.PostAsync(Route(runId), Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RunLifecycle.Created, (await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId))).Lifecycle);
        Assert.Equal(0, await WithDbAsync(db => db.Events.CountAsync(e => e.RunId == runId && e.EventType == RunEventType.RunAbandoned)));
    }

    // The wire form of a supplementary-plane Format character is a JSON surrogate-pair escape: U+E0001 is 󠀁, U+E0020 is
    // 󠀠, U+E007F is 󠁿 and U+1D173 is 𝅳. The host must classify the decoded scalar, as the client does.
    [Theory]
    [InlineData("""{"reason":"before󠀁after"}""")]
    [InlineData("""{"reason":"before󠀠after"}""")]
    [InlineData("""{"reason":"before󠁿after"}""")]
    [InlineData("""{"reason":"before𝅳after"}""")]
    [InlineData("""{"reason":"😀󠀁"}""")]
    public async Task A_supplementary_format_character_is_a_bad_request_that_writes_nothing(string body)
    {
        using var client = Client();
        var (_, runId) = await SeedManualRunAsync();

        var response = await client.PostAsync(Route(runId), Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RunLifecycle.Created, (await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId))).Lifecycle);
        Assert.Null((await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId))).AbandonmentReason);
        Assert.Equal(0, await WithDbAsync(db => db.Events.CountAsync(e => e.RunId == runId && e.EventType == RunEventType.RunAbandoned)));
        Assert.Equal(1, await WithDbAsync(db => db.Events.CountAsync(e => e.RunId == runId)));
    }

    [Fact]
    public async Task Ordinary_supplementary_text_is_accepted_and_recorded_exactly()
    {
        using var client = Client();
        var (_, runId) = await SeedManualRunAsync();
        var reason = "Replaced " + char.ConvertFromUtf32(0x1F600) + char.ConvertFromUtf32(0x20000) + " by a smaller change";

        var response = await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = reason });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(reason, (await response.Content.ReadFromJsonAsync<AbandonManualRunResponse>())!.Reason);
        Assert.Equal(reason, (await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId))).AbandonmentReason);
    }

    [Fact]
    public async Task A_reason_of_exactly_two_kibibytes_is_accepted_and_one_byte_more_is_refused()
    {
        using var client = Client();
        var (_, exactRun) = await SeedManualRunAsync();
        var (_, longRun) = await SeedManualRunAsync();

        var exact = await client.PostAsJsonAsync(Route(exactRun), new AbandonManualRunRequest { Reason = new string('x', 2048) });
        var tooLong = await client.PostAsJsonAsync(Route(longRun), new AbandonManualRunRequest { Reason = new string('x', 2049) });

        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(RunLifecycle.Created, (await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == longRun))).Lifecycle);
    }

    private static int FreeLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task The_real_host_refuses_a_body_beyond_eight_kibibytes_and_still_accepts_one_inside_it()
    {
        await using var kestrel = new ApiWebApplicationFactory();
        kestrel.UseKestrel(FreeLoopbackPort());
        kestrel.StartServer();
        using var client = kestrel.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        Guid refusedRun;
        Guid acceptedRun;
        using (var scope = kestrel.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var firstProject = Project.Register(Guid.NewGuid(), "Bound", $@"C:\repos{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
            var secondProject = Project.Register(Guid.NewGuid(), "Bound two", $@"C:\repos{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
            var first = Run.RecordClassifiedIntent(
                Guid.NewGuid(), firstProject.Id, firstProject.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "One", DateTimeOffset.UtcNow);
            var second = Run.RecordClassifiedIntent(
                Guid.NewGuid(), secondProject.Id, secondProject.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Two", DateTimeOffset.UtcNow);
            db.Projects.AddRange(firstProject, secondProject);
            db.Runs.AddRange(first, second);
            await db.SaveChangesAsync();
            refusedRun = first.Id;
            acceptedRun = second.Id;
        }

        var oversized = await client.PostAsync(
            Route(refusedRun), Json("{\"reason\":\"ok\",\"padding\":\"" + new string('x', 9 * 1024) + "\"}"));
        var inside = await client.PostAsync(
            Route(acceptedRun), Json("{\"reason\":\"ok\",\"padding\":\"" + new string('x', 4 * 1024) + "\"}"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        using var verify = kestrel.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Equal(RunLifecycle.Created, (await verifyDb.Runs.AsNoTracking().SingleAsync(run => run.Id == refusedRun)).Lifecycle);
        Assert.Equal(RunLifecycle.Abandoned, (await verifyDb.Runs.AsNoTracking().SingleAsync(run => run.Id == acceptedRun)).Lifecycle);
    }

    // ---- refusals --------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unknown_run_is_a_safe_not_found_for_both_operations()
    {
        using var client = Client();

        var post = await client.PostAsJsonAsync(Route(Guid.NewGuid()), new AbandonManualRunRequest { Reason = Reason });
        var get = await client.GetAsync($"/api/runs/{Guid.NewGuid()}/abandonment");

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.DoesNotContain("Exception", await post.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RunExecutionMode.Legacy, "run_abandonment.run_not_manual")]
    [InlineData(RunExecutionMode.Simulated, "run_abandonment.run_not_manual")]
    public async Task A_legacy_or_simulated_run_is_refused_with_a_fixed_code_and_nothing_is_written(RunExecutionMode mode, string code)
    {
        using var client = Client();
        var (_, runId) = await SeedManualRunAsync(mode: mode);

        var response = await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = Reason });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await ErrorCodeAsync(response));
        var run = await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == runId));
        Assert.Equal(RunLifecycle.Created, run.Lifecycle);
        Assert.Null(run.AbandonmentReason);
        var status = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{runId}/abandonment"))!;
        Assert.False(status.Eligible);
        Assert.Equal(code, status.RefusalCode);
    }

    [Fact]
    public async Task A_terminal_run_is_refused_and_an_active_attempt_blocks_with_its_own_fixed_code()
    {
        using var client = Client();
        var (_, terminal) = await SeedManualRunAsync(running: true);
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET Lifecycle = 'Completed' WHERE Id = {terminal}");
            return 0;
        });
        var (_, busy) = await SeedManualRunAsync(running: true);
        await WithDbAsync(async db =>
        {
            db.Attempts.Add(Attempt.Claim(Guid.NewGuid(), busy, 1, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return 0;
        });

        var terminalResponse = await client.PostAsJsonAsync(Route(terminal), new AbandonManualRunRequest { Reason = Reason });
        var busyResponse = await client.PostAsJsonAsync(Route(busy), new AbandonManualRunRequest { Reason = Reason });

        Assert.Equal("run_abandonment.run_not_abandonable", await ErrorCodeAsync(terminalResponse));
        Assert.Equal("run_abandonment.active_attempt", await ErrorCodeAsync(busyResponse));
        var busyStatus = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{busy}/abandonment"))!;
        Assert.False(busyStatus.Eligible);
        Assert.Equal("run_abandonment.active_attempt", busyStatus.RefusalCode);
        Assert.Equal(RunLifecycle.Running, (await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == busy))).Lifecycle);
    }

    // ---- the read-only status ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_status_of_an_eligible_run_is_read_only_and_repeatable()
    {
        using var client = Client();
        var (_, runId) = await SeedManualRunAsync(running: true);
        var eventsBefore = await WithDbAsync(db => db.Events.CountAsync(e => e.RunId == runId));

        var first = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{runId}/abandonment"))!;
        var second = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{runId}/abandonment"))!;

        Assert.True(first.Eligible);
        Assert.Null(first.RefusalCode);
        Assert.Null(first.Abandonment);
        Assert.Equal(first, second);
        Assert.Equal(eventsBefore, await WithDbAsync(db => db.Events.CountAsync(e => e.RunId == runId)));
        Assert.Equal(RunLifecycle.Running, (await WithDbAsync(db => db.Runs.AsNoTracking().SingleAsync(run => run.Id == runId))).Lifecycle);
    }

    // ---- a damaged stored abandonment time --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026-13-45 99:99:99+00:00")]
    [InlineData("")]
    public async Task A_damaged_stored_abandonment_time_keeps_that_run_incoherent_and_breaks_no_read(string stored)
    {
        using var client = Client();
        var (damagedProject, damaged) = await SeedManualRunAsync(running: true);
        var (siblingProject, sibling) = await SeedManualRunAsync(running: true);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Route(damaged), new AbandonManualRunRequest { Reason = Reason })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Route(sibling), new AbandonManualRunRequest { Reason = Reason })).StatusCode);
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET AbandonedAtUtc = {stored} WHERE Id = {damaged}"));

        var status = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{damaged}/abandonment"))!;
        var replay = await client.PostAsJsonAsync(Route(damaged), new AbandonManualRunRequest { Reason = Reason });
        var conflict = await client.PostAsJsonAsync(Route(damaged), new AbandonManualRunRequest { Reason = "A different reason." });
        var cockpit = await client.GetAsync($"/api/runs/{damaged}/cockpit");
        var intake = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(damagedProject, "Another objective"));
        var summaries = (await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries"))!;
        var siblingStatus = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{sibling}/abandonment"))!;

        Assert.False(status.Eligible);
        Assert.Equal("run_abandonment.abandonment_incoherent", status.RefusalCode);
        Assert.Null(status.Abandonment);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal("run_abandonment.abandonment_incoherent", await ErrorCodeAsync(replay));
        Assert.Equal("run_abandonment.abandonment_incoherent", await ErrorCodeAsync(conflict));
        Assert.Equal(HttpStatusCode.OK, cockpit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, intake.StatusCode);
        Assert.False(summaries.Single(candidate => candidate.ProjectId == damagedProject).CanCreateRun);
        Assert.True(summaries.Single(candidate => candidate.ProjectId == siblingProject).CanCreateRun);
        Assert.Equal(Reason, siblingStatus.Abandonment!.Reason);
        Assert.Equal(1, await WithDbAsync(db => db.Runs.CountAsync(run => run.ProjectId == damagedProject)));
        Assert.Equal(1, await WithDbAsync(db => db.Events.CountAsync(e => e.RunId == damaged && e.EventType == RunEventType.RunAbandoned)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_blob_holding_the_exact_stored_time_is_a_damaged_row_for_every_route_while_the_identical_text_is_a_closure(bool asBlob)
    {
        using var client = Client();
        var (project, runId) = await SeedManualRunAsync(running: true);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = Reason })).StatusCode);
        if (asBlob)
        {
            await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE runs SET AbandonedAtUtc = CAST(AbandonedAtUtc AS BLOB) WHERE Id = {runId}"));
        }

        var storedClass = await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleAsync());
        var storedBytes = await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT hex(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleAsync());
        var status = (await client.GetFromJsonAsync<GetManualRunAbandonmentResponse>($"/api/runs/{runId}/abandonment"))!;
        var replay = await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = Reason });
        var conflict = await client.PostAsJsonAsync(Route(runId), new AbandonManualRunRequest { Reason = "A different reason." });
        var cockpit = await client.GetAsync($"/api/runs/{runId}/cockpit");
        var intake = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(project, "Another objective"));
        var summaries = (await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries"))!;

        Assert.Equal(asBlob ? "blob" : "text", storedClass);
        Assert.Equal(HttpStatusCode.OK, cockpit.StatusCode);
        if (asBlob)
        {
            Assert.Equal("run_abandonment.abandonment_incoherent", status.RefusalCode);
            Assert.Null(status.Abandonment);
            Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
            Assert.Equal("run_abandonment.abandonment_incoherent", await ErrorCodeAsync(replay));
            Assert.Equal("run_abandonment.abandonment_incoherent", await ErrorCodeAsync(conflict));
            Assert.Equal(HttpStatusCode.Conflict, intake.StatusCode);
            Assert.False(summaries.Single(candidate => candidate.ProjectId == project).CanCreateRun);
            Assert.Equal(1, await WithDbAsync(db => db.Runs.CountAsync(run => run.ProjectId == project)));
        }
        else
        {
            Assert.Equal(Reason, status.Abandonment!.Reason);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal("run_abandonment.reason_conflict", await ErrorCodeAsync(conflict));
            Assert.Equal(HttpStatusCode.OK, intake.StatusCode);
        }

        Assert.Equal(storedClass, await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT typeof(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleAsync()));
        Assert.Equal(storedBytes, await WithDbAsync(db => db.Database.SqlQuery<string>($"SELECT hex(AbandonedAtUtc) AS Value FROM runs WHERE Id = {runId}").SingleAsync()));
    }
}
