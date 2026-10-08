using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.Features.Projects.GetCheckpointApprovalEvidence;
using DevalCopilot.Api.Features.Projects.RecordCheckpointReview;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0030's two protected operations over the real host and a file-backed SQLite database: the optional execution-set form of the
/// existing review POST (binding, count, duplicate, ambiguity, body bound, ownership) and the read-only approval-evidence GET.
/// Every refusal persists nothing and never echoes a stored or submitted value.
/// </summary>
public sealed class CheckpointApprovalEvidenceEndpointTests(ReviewApiWebApplicationFactory factory) : IClassFixture<ReviewApiWebApplicationFactory>
{
    private const string ProjectsRoute = "/api/projects";
    private static readonly string Fingerprint = new('a', 64);

    private HttpClient Client()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private sealed record Seeded(Guid ProjectId, Guid CheckpointId, Guid UnitCommandId, Guid LintCommandId, Guid UnitExecutionId, Guid LintExecutionId);

    private Task<Seeded> SeedAsync(bool secondRecipe = true) => SeedAsync(factory, secondRecipe);

    private static async Task<Seeded> SeedAsync(ReviewApiWebApplicationFactory host, bool secondRecipe = true)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "Approval project", $@"C:\repos\{Guid.NewGuid():N}", now);
        var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), Fingerprint, []);
        var unit = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 1, "Unit", @"C:\dotnet.exe", ["test"], 60, true, now);
        var lint = VerificationCommand.Configure(Guid.NewGuid(), project.Id, 2, "Lint", @"C:\dotnet.exe", ["format"], 60, secondRecipe, now);
        var unitRun = Passed(project, workspace, checkpoint, unit, 1, now);
        var lintRun = Passed(project, workspace, checkpoint, lint, 2, now);
        db.Projects.Add(project);
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(checkpoint);
        db.VerificationCommands.AddRange(unit, lint);
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        db.VerificationExecutions.AddRange(unitRun, lintRun);
        await db.SaveChangesAsync(CancellationToken.None);
        return new Seeded(project.Id, checkpoint.Id, unit.Id, lint.Id, unitRun.Id, lintRun.Id);
    }

    private static VerificationExecution Passed(
        Project project, GitWorkspace workspace, GitCheckpoint checkpoint, VerificationCommand command, int number, DateTimeOffset now)
    {
        var execution = VerificationExecution.Claim(Guid.NewGuid(), project.Id, number, workspace, checkpoint, command, now);
        execution.MarkDispatched(now);
        execution.Complete(VerificationExecutionOutcome.Exited, 0, checkpoint.FingerprintSha256, now);
        return execution;
    }

    private Task<(int Reviews, int Members)> CountsAsync(Guid projectId) => CountsAsync(factory, projectId);

    private static async Task<(int Reviews, int Members)> CountsAsync(ReviewApiWebApplicationFactory host, Guid projectId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        return (
            await db.CheckpointReviews.CountAsync(review => review.ProjectId == projectId),
            await db.CheckpointReviewEvidence.CountAsync(member => db.CheckpointReviews.Any(review => review.Id == member.CheckpointReviewId && review.ProjectId == projectId)));
    }

    private static int FreeLoopbackPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string ReviewBody(Seeded data, string decision, string evidence) =>
        $$"""{"gitCheckpointId":"{{data.CheckpointId}}","actorKind":"Human","decision":"{{decision}}",{{evidence}}}""";

    // ---- The approval-evidence GET -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_bundle_requires_an_authenticated_session()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"{ProjectsRoute}/{Guid.NewGuid()}/checkpoints/{Guid.NewGuid()}/approval-evidence");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_complete_bundle_is_returned_bound_to_its_source_in_command_order()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.GetAsync($"{ProjectsRoute}/{data.ProjectId}/checkpoints/{data.CheckpointId}/approval-evidence");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bundle = (await response.Content.ReadFromJsonAsync<CheckpointApprovalEvidenceResponse>())!;
        Assert.Equal((data.ProjectId, data.CheckpointId, 1, Fingerprint), (bundle.ProjectId, bundle.CheckpointId, bundle.CheckpointNumber, bundle.FingerprintSha256));
        Assert.Equal(
            [(data.UnitCommandId, 1, "Unit", data.UnitExecutionId, 1), (data.LintCommandId, 2, "Lint", data.LintExecutionId, 2)],
            bundle.Members.Select(member => (member.VerificationCommandId, member.CommandNumber, member.RecipeLabel, member.VerificationExecutionId, member.ExecutionNumber)));
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task The_bundle_for_an_unknown_checkpoint_is_a_safe_conflict_and_for_an_unknown_project_a_safe_not_found()
    {
        using var client = Client();
        var data = await SeedAsync();

        var conflict = await client.GetAsync($"{ProjectsRoute}/{data.ProjectId}/checkpoints/{Guid.NewGuid()}/approval-evidence");
        var notFound = await client.GetAsync($"{ProjectsRoute}/{Guid.NewGuid()}/checkpoints/{data.CheckpointId}/approval-evidence");

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("reviews.checkpoint_not_current", await conflict.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        var body = await notFound.Content.ReadAsStringAsync();
        Assert.DoesNotContain("C:\\", body);
        Assert.DoesNotContain("System.", body);
    }

    [Fact]
    public async Task An_incomplete_set_is_a_fixed_conflict_without_a_partial_bundle()
    {
        using var client = Client();
        var data = await SeedAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await db.VerificationExecutions.Where(execution => execution.Id == data.LintExecutionId)
                .ExecuteUpdateAsync(set => set.SetProperty(execution => execution.ExitCode, 9));
        }

        var response = await client.GetAsync($"{ProjectsRoute}/{data.ProjectId}/checkpoints/{data.CheckpointId}/approval-evidence");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("approval_evidence.verification_incomplete", body);
        Assert.DoesNotContain("members", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", body);
    }

    // ---- The review POST: the set form -------------------------------------------------------------------------------

    [Fact]
    public async Task The_complete_set_is_recorded_as_one_review_with_both_members()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsJsonAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            new RecordCheckpointReviewRequest(
                data.CheckpointId, null, "Human", "Approved", [data.LintExecutionId, data.UnitExecutionId]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var review = Assert.Single(db.CheckpointReviews.Include(candidate => candidate.Evidence).Where(candidate => candidate.ProjectId == data.ProjectId));
        Assert.Equal(
            new[] { (data.UnitCommandId, data.UnitExecutionId), (data.LintCommandId, data.LintExecutionId) }.ToHashSet(),
            review.Evidence.Select(member => (member.VerificationCommandId, member.VerificationExecutionId)).ToHashSet());
    }

    [Fact]
    public async Task The_legacy_scalar_form_is_unchanged()
    {
        using var client = Client();
        var data = await SeedAsync(secondRecipe: false);

        var response = await client.PostAsJsonAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            new { GitCheckpointId = data.CheckpointId, VerificationExecutionId = data.UnitExecutionId, ActorKind = "Human", Decision = "Approved" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((1, 1), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task Both_evidence_forms_are_refused_as_ambiguous_without_persisting()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json(ReviewBody(
                data,
                "Approved",
                $"\"verificationExecutionId\":\"{data.UnitExecutionId}\",\"verificationExecutionIds\":[\"{data.LintExecutionId}\"]")));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("reviews.evidence_forms_ambiguous", body);
        Assert.DoesNotContain(data.UnitExecutionId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task More_than_thirty_two_identifiers_are_refused_without_persisting()
    {
        using var client = Client();
        var data = await SeedAsync();
        var many = string.Join(',', Enumerable.Range(0, 33).Select(_ => $"\"{Guid.NewGuid()}\""));

        var response = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews", Json(ReviewBody(data, "Approved", $"\"verificationExecutionIds\":[{many}]")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("reviews.evidence_selection_invalid", await response.Content.ReadAsStringAsync());
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task A_duplicate_identifier_is_refused_without_persisting()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json(ReviewBody(data, "Approved", $"\"verificationExecutionIds\":[\"{data.UnitExecutionId}\",\"{data.UnitExecutionId}\"]")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("reviews.evidence_selection_invalid", await response.Content.ReadAsStringAsync());
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Theory]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"", 422)]
    [InlineData("\"\"", 400)]
    [InlineData("\"not-a-guid\"", 400)]
    [InlineData("null", 400)]
    [InlineData("42", 400)]
    public async Task A_malformed_or_empty_identifier_is_refused_without_persisting(string element, int expected)
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json(ReviewBody(data, "Approved", $"\"verificationExecutionIds\":[\"{data.UnitExecutionId}\",{element}]")));

        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task A_non_array_value_is_refused_without_persisting()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json(ReviewBody(data, "Approved", $"\"verificationExecutionIds\":\"{data.UnitExecutionId}\"")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task A_decided_review_with_an_empty_set_is_refused_but_pending_with_an_empty_set_is_recorded()
    {
        using var client = Client();
        var data = await SeedAsync();

        var decided = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews", Json(ReviewBody(data, "Approved", "\"verificationExecutionIds\":[]")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, decided.StatusCode);
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));

        var pending = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews", Json(ReviewBody(data, "Pending", "\"verificationExecutionIds\":[]")));
        Assert.Equal(HttpStatusCode.Created, pending.StatusCode);
        Assert.Equal((1, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task A_pending_review_with_members_is_the_existing_conflict()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json(ReviewBody(data, "Pending", $"\"verificationExecutionIds\":[\"{data.UnitExecutionId}\"]")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("reviews.pending_cannot_include_evidence", await response.Content.ReadAsStringAsync());
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task The_set_form_is_refused_for_a_non_human_reviewer()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsJsonAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            new RecordCheckpointReviewRequest(data.CheckpointId, null, "FutureAgent", "Approved", [data.UnitExecutionId, data.LintExecutionId]));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("reviews.evidence_set_requires_human", await response.Content.ReadAsStringAsync());
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
    }

    [Fact]
    public async Task A_subset_is_an_incomplete_set_conflict_and_a_foreign_project_execution_is_not_found()
    {
        using var client = Client();
        var data = await SeedAsync();
        var other = await SeedAsync(secondRecipe: false);

        var subset = await client.PostAsJsonAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            new RecordCheckpointReviewRequest(data.CheckpointId, null, "Human", "Approved", [data.UnitExecutionId]));
        var foreign = await client.PostAsJsonAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            new RecordCheckpointReviewRequest(data.CheckpointId, null, "Human", "Approved", [data.UnitExecutionId, other.UnitExecutionId]));

        Assert.Equal(HttpStatusCode.Conflict, subset.StatusCode);
        Assert.Contains("reviews.approval_requires_complete_verification_set", await subset.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        var body = await foreign.Content.ReadAsStringAsync();
        Assert.Contains("reviews.evidence_not_found", body);
        Assert.DoesNotContain(other.UnitExecutionId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, 0), await CountsAsync(data.ProjectId));
        Assert.Equal((0, 0), await CountsAsync(other.ProjectId));
    }

    [Fact]
    public async Task A_request_body_over_eight_kibibytes_is_refused_by_the_real_host_before_binding_and_persists_nothing()
    {
        // The 8 KiB MVC request-body bound is proven through real Kestrel (the in-memory TestServer has no server size feature).
        // A loopback port chosen by the operating system for this test alone: the older Kestrel tests share the default port and run in
        // parallel with every other test class, so this one must not compete for it.
        await using var kestrel = new ReviewApiWebApplicationFactory();
        kestrel.UseKestrel(FreeLoopbackPort());
        kestrel.StartServer();
        using var client = kestrel.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var data = await SeedAsync(kestrel);
        var padding = new string('x', 9 * 1024);

        var oversized = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json($$"""{"gitCheckpointId":"{{data.CheckpointId}}","actorKind":"Human","decision":"Pending","note":"{{padding}}"}"""));
        var inside = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json($$"""{"gitCheckpointId":"{{data.CheckpointId}}","actorKind":"Human","decision":"Pending","note":"{{new string('x', 4 * 1024)}}"}"""));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.Created, inside.StatusCode);
        Assert.Equal((1, 0), await CountsAsync(kestrel, data.ProjectId));
    }

    [Fact]
    public async Task A_body_just_under_the_bound_with_thirty_two_identifiers_is_accepted_for_binding()
    {
        using var client = Client();
        var data = await SeedAsync();
        var many = string.Join(',', Enumerable.Range(0, 32).Select(_ => $"\"{Guid.NewGuid()}\""));
        var body = ReviewBody(data, "Approved", $"\"verificationExecutionIds\":[{many}]");
        Assert.True(Encoding.UTF8.GetByteCount(body) < 8 * 1024);

        var response = await client.PostAsync($"{ProjectsRoute}/{data.ProjectId}/reviews", Json(body));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("reviews.evidence_not_found", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_review_response_and_error_bodies_never_echo_submitted_text()
    {
        using var client = Client();
        var data = await SeedAsync();

        var response = await client.PostAsync(
            $"{ProjectsRoute}/{data.ProjectId}/reviews",
            Json(ReviewBody(data, "Approved", $"\"verificationExecutionId\":\"{data.UnitExecutionId}\",\"verificationExecutionIds\":[]")));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var parsed = JsonDocument.Parse(text);
        Assert.DoesNotContain(data.CheckpointId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(parsed);
    }
}
