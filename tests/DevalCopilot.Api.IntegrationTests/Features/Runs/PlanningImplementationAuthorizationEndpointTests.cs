using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.Features.Runs.AuthorizePlanningImplementation;
using DevalCopilot.Api.Features.Runs.RequestImplementation;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Runs;
using DevalCopilot.Application.Features.Runs.Commands.CreateChallengeResolutionAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using DevalCopilot.Application.Features.Runs.Commands.CreateCodexPlanningAttempt;
using DevalCopilot.Application.Features.Runs.Commands.MarkAgentAttemptDispatched;
using DevalCopilot.Application.Features.Runs.Commands.RecordAgentAttemptResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using DevalCopilot.Application.Features.Runs.Commands.RecordClaudeCriticalReviewResult;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// <c>POST/GET …/planning-escalations/{escalationMessageId}/implementation-authorization</c> (ADR-0016): protected,
/// explicit, bounded, truthful about idempotency, conflicts, and state, and never echoing the rationale. The lineage is
/// written by production commands so the escalation is exactly what production records.
/// </summary>
public sealed class PlanningImplementationAuthorizationEndpointTests : IDisposable
{
    private const string Sentinel = "SENTINEL-ENDPOINT-3 I accept the final plan.";

    private static readonly string Fingerprint = CodeReviewApiWebApplicationFactory.MatchingFingerprint;

    private static readonly string ProposalJson = JsonSerializer.Serialize(new
    {
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private readonly CodeReviewApiWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private HttpClient AuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    private static string Url(Guid runId, Guid escalationId) =>
        $"/api/runs/{runId}/planning-escalations/{escalationId}/implementation-authorization";

    private static StringContent Body(string rationale) =>
        new(JsonSerializer.Serialize(new AuthorizePlanningImplementationRequest(rationale)), Encoding.UTF8, "application/json");

    private sealed record Seed(Guid RunId, Guid EscalationId, Guid FinalId, Guid RootId, IReadOnlyList<Guid> DecisionIds);

    private async Task<Seed> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IApplicationMediator>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", now);
        db.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), "Implement the ledger", now);
        run.Claim(now);
        db.Runs.Add(run);
        var workspace = GitWorkspace.Prepare(
            Guid.NewGuid(), project.Id, 1, Path.Combine(Path.GetTempPath(), $"devalcopilot-endpoint-ws-{Guid.NewGuid():N}"),
            "branch", new string('a', 40), "main", now);
        workspace.MarkReady();
        db.GitWorkspaces.Add(workspace);
        db.GitCheckpoints.Add(GitCheckpoint.Capture(
            Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []));
        db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
        foreach (var (capability, path) in new[] { (Capability.CodexCli, @"C:\fake\codex.exe"), (Capability.ClaudeCli, @"C:\fake\claude.exe") })
        {
            // The host seeds the capability catalog at startup, so an existing snapshot is marked observed instead.
            var existing = await db.HostCapabilitySnapshots.SingleOrDefaultAsync(item => item.Capability == capability);
            var snapshot = existing ?? HostCapabilitySnapshot.Seed(capability, now);
            snapshot.MarkDispatched(now);
            snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, path, null, "1.0.0", now, now.AddMinutes(5));
            if (existing is null)
            {
                db.HostCapabilitySnapshots.Add(snapshot);
            }
        }

        await db.SaveChangesAsync();

        var planning = await mediator.SendAsync(new CreateCodexPlanningAttemptCommand(run.Id), CancellationToken.None);
        Assert.True(planning.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(run.Id, planning.Value.AttemptId), CancellationToken.None)).IsSuccess);
        Assert.True((await mediator.SendAsync(
            new RecordAgentAttemptResultCommand(
                run.Id, planning.Value.AttemptId, AgentOutcome.Proposed, Fingerprint, [],
                new ValidatedProposal("Add the ledger table and its query.", ProposalJson), null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var root = await db.CollaborationMessages.SingleAsync(
            message => message.AttemptId == planning.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);
        var first = await RoundAsync(mediator, db, run.Id, root.Id);
        var second = await RoundAsync(mediator, db, run.Id, first.RevisedId);
        var escalation = await db.CollaborationMessages.SingleAsync(
            message => message.RunId == run.Id && message.Type == CollaborationMessageType.Escalation);
        var decisions = await db.CollaborationMessages
            .Where(message => message.AttemptId == second.ResolverAttemptId && message.Type == CollaborationMessageType.Decision)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();
        return new Seed(run.Id, escalation.Id, second.RevisedId, root.Id, decisions);
    }

    private sealed record Round(Guid ResolverAttemptId, Guid RevisedId);

    private static async Task<Round> RoundAsync(IApplicationMediator mediator, DevalCopilotDbContext db, Guid runId, Guid proposalId)
    {
        var claim = await mediator.SendAsync(new CreateClaudeCriticalReviewAttemptCommand(runId, proposalId), CancellationToken.None);
        Assert.True(claim.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, claim.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var review = ClaudeCriticalReviewResponseParser.TryParse(JsonSerializer.Serialize(new
        {
            decision = "challenge",
            summary = "Material issues were found.",
            challenges = new[]
            {
                new
                {
                    summary = "Challenge 1 raises a material concern.",
                    disputedItem = "Step 1",
                    materialImpact = "Could cause data loss",
                    reasoning = "The step does not account for concurrent writers",
                    alternativeOrQuestion = "Consider a serialized write path instead",
                },
            },
        }));
        Assert.NotNull(review);
        Assert.True((await mediator.SendAsync(
            new RecordClaudeCriticalReviewResultCommand(
                runId, claim.Value.AttemptId, AgentOutcome.Challenged, Fingerprint, [], review, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var challengeIds = await db.CollaborationMessages
            .Where(message => message.AttemptId == claim.Value.AttemptId && message.Type == CollaborationMessageType.Challenge)
            .OrderBy(message => message.Sequence).Select(message => message.Id).ToListAsync();
        var resolver = await mediator.SendAsync(new CreateChallengeResolutionAttemptCommand(runId, claim.Value.AttemptId), CancellationToken.None);
        Assert.True(resolver.IsSuccess);
        Assert.True((await mediator.SendAsync(new MarkAgentAttemptDispatchedCommand(runId, resolver.Value.AttemptId), CancellationToken.None)).IsSuccess);
        var resolution = ChallengeResolutionResponseParser.TryParse(JsonSerializer.Serialize(new
        {
            summary = "Every challenge has been resolved.",
            decisions = challengeIds.Select(id => new
            {
                challengeMessageId = id.ToString(),
                summary = "Decision accepts the challenge.",
                resolution = ChallengeResolutionOutputSchema.AcceptedResolution,
                rationale = "Rationale",
                resultingPlanChanges = "Plan changes",
                nextAction = "Next action",
            }),
            revisedProposal = new
            {
                summary = "Revised proposal addressing every challenge.",
                scope = "Revised scope",
                implementationSteps = "Revised steps",
                risks = "Revised risks",
                verificationPlan = "Revised verification",
                escalationPoints = "Revised escalation",
            },
        }), challengeIds.ToHashSet());
        Assert.NotNull(resolution);
        Assert.True((await mediator.SendAsync(
            new RecordChallengeResolutionResultCommand(
                runId, resolver.Value.AttemptId, AgentOutcome.Resolved, Fingerprint, [], resolution, null,
                ProcessEvidence: TestProcessEvidence.ReportedCleanExit),
            CancellationToken.None)).IsSuccess);
        var revised = await db.CollaborationMessages.SingleAsync(
            message => message.AttemptId == resolver.Value.AttemptId && message.Type == CollaborationMessageType.Proposal);
        return new Round(resolver.Value.AttemptId, revised.Id);
    }

    private static void AssertNoDisclosure(string body)
    {
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Fingerprint, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_operations_require_authentication()
    {
        using var client = _factory.CreateClient();

        var post = await client.PostAsync(Url(Guid.NewGuid(), Guid.NewGuid()), Body(Sentinel));
        var get = await client.GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }

    [Fact]
    public async Task An_unknown_run_or_escalation_is_a_safe_not_found_for_both_operations()
    {
        using var client = AuthenticatedClient();
        var seed = await SeedAsync();

        foreach (var url in new[] { Url(Guid.NewGuid(), Guid.NewGuid()), Url(seed.RunId, Guid.NewGuid()), Url(seed.RunId, seed.FinalId) })
        {
            var post = await client.PostAsync(url, Body(Sentinel));
            var get = await client.GetAsync(url);

            Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            AssertNoDisclosure(await post.Content.ReadAsStringAsync());
            AssertNoDisclosure(await get.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_missing_body_is_rejected_and_records_nothing()
    {
        using var client = AuthenticatedClient();
        var seed = await SeedAsync();

        var response = await client.PostAsync(Url(seed.RunId, seed.EscalationId), content: null);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType, response.StatusCode.ToString());
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().PlanningImplementationAuthorizations.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SENTINEL-9001 contains the api key marker")]
    [InlineData("SENTINEL-9002 reads C:\\Users\\me")]
    public async Task An_invalid_rationale_is_a_400_that_never_echoes_it_and_records_nothing(string rationale)
    {
        using var client = AuthenticatedClient();
        var seed = await SeedAsync();

        var response = await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body(rationale));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("planning_authorizations.rationale_invalid", body, StringComparison.Ordinal);
        AssertNoDisclosure(body);
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().PlanningImplementationAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task An_overlong_or_oversized_request_is_refused_without_echo()
    {
        using var client = AuthenticatedClient();
        var seed = await SeedAsync();

        var overlong = await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body("SENTINEL-1 " + new string('x', 600)));
        var huge = await client.PostAsync(
            Url(seed.RunId, seed.EscalationId),
            new StringContent(JsonSerializer.Serialize(new { rationale = "SENTINEL-77 " + new string('x', 64 * 1024) }), Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.True((int)huge.StatusCode is 400 or 413, huge.StatusCode.ToString());
        AssertNoDisclosure(await overlong.Content.ReadAsStringAsync());
        AssertNoDisclosure(await huge.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().PlanningImplementationAuthorizations.ToListAsync());
    }

    [Fact]
    public async Task The_state_is_absent_then_available_with_the_exact_reason_and_the_post_is_idempotent_or_conflicting()
    {
        using var client = AuthenticatedClient();
        var seed = await SeedAsync();

        using var absent = JsonDocument.Parse(await (await client.GetAsync(Url(seed.RunId, seed.EscalationId))).Content.ReadAsStringAsync());
        Assert.Equal("Absent", absent.RootElement.GetProperty("state").GetString());
        Assert.Equal(seed.FinalId, absent.RootElement.GetProperty("finalProposalMessageId").GetGuid());
        Assert.Equal(seed.DecisionIds, absent.RootElement.GetProperty("orderedDecisionMessageIds").EnumerateArray().Select(id => id.GetGuid()));

        var first = await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body(Sentinel));
        var firstBody = await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        AssertNoDisclosure(firstBody);
        using var firstJson = JsonDocument.Parse(firstBody);
        Assert.Equal("Authorized", firstJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(6, firstJson.RootElement.EnumerateObject().Count());
        Assert.Equal(seed.FinalId, firstJson.RootElement.GetProperty("finalProposalMessageId").GetGuid());
        var authorizationId = firstJson.RootElement.GetProperty("authorizationId").GetGuid();

        var identical = await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body("  " + Sentinel + "  "));
        using var identicalJson = JsonDocument.Parse(await identical.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, identical.StatusCode);
        Assert.Equal(authorizationId, identicalJson.RootElement.GetProperty("authorizationId").GetGuid());

        var different = await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body("A different SENTINEL-5150 reason."));
        var differentBody = await different.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Contains("planning_authorizations.rationale_conflict", differentBody, StringComparison.Ordinal);
        AssertNoDisclosure(differentBody);

        using var available = JsonDocument.Parse(await (await client.GetAsync(Url(seed.RunId, seed.EscalationId))).Content.ReadAsStringAsync());
        Assert.Equal("Available", available.RootElement.GetProperty("state").GetString());
        Assert.Equal(Sentinel, available.RootElement.GetProperty("rationale").GetString());
        Assert.Equal(authorizationId, available.RootElement.GetProperty("authorizationId").GetGuid());
        Assert.Equal(JsonValueKind.Null, available.RootElement.GetProperty("consumedByAttemptId").ValueKind);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Single(await db.PlanningImplementationAuthorizations.ToListAsync());
        Assert.Single(await db.CollaborationMessages.Where(m => m.Type == CollaborationMessageType.HumanInstruction).ToListAsync());
    }

    [Fact]
    public async Task The_explicit_implementation_request_is_refused_before_authorization_and_consumes_the_grant_after_it()
    {
        using var client = AuthenticatedClient();
        var seed = await SeedAsync();
        var implementation = $"/api/runs/{seed.RunId}/agent-attempts/implementation";

        var refused = await client.PostAsJsonAsync(implementation, new RequestImplementationRequest(seed.FinalId));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("agent_attempts.proposal_lineage_exhausted", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body(Sentinel))).StatusCode);
        var accepted = await client.PostAsJsonAsync(implementation, new RequestImplementationRequest(seed.FinalId));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var acceptedJson = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var attemptId = acceptedJson.RootElement.GetProperty("attemptId").GetGuid();

        using var consumed = JsonDocument.Parse(await (await client.GetAsync(Url(seed.RunId, seed.EscalationId))).Content.ReadAsStringAsync());
        Assert.Equal("Consumed", consumed.RootElement.GetProperty("state").GetString());
        Assert.Equal(attemptId, consumed.RootElement.GetProperty("consumedByAttemptId").GetGuid());

        var again = await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body(Sentinel));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("planning_authorizations.already_consumed", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var second = await client.PostAsJsonAsync(implementation, new RequestImplementationRequest(seed.FinalId));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task A_tampered_recorded_instruction_reads_as_invalid_and_is_refused_by_a_retry()
    {
        using var client = AuthenticatedClient();
        var seed = await SeedAsync();
        using var posted = JsonDocument.Parse(await (await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body(Sentinel))).Content.ReadAsStringAsync());
        var instructionId = posted.RootElement.GetProperty("humanInstructionMessageId").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>().CollaborationMessages
                .Where(message => message.Id == instructionId)
                .ExecuteUpdateAsync(set => set.SetProperty(message => message.StructuredContentJson, "{\"instruction\":\"x\",\"rationale\":\"y\"}"));
        }

        using var invalid = JsonDocument.Parse(await (await client.GetAsync(Url(seed.RunId, seed.EscalationId))).Content.ReadAsStringAsync());
        var retry = await client.PostAsync(Url(seed.RunId, seed.EscalationId), Body(Sentinel));

        Assert.Equal("Invalid", invalid.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, invalid.RootElement.GetProperty("rationale").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Contains("planning_authorizations.recorded_invalid", await retry.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
