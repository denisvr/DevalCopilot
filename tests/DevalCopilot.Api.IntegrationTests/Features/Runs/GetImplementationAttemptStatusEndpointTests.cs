using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

public sealed class GetImplementationAttemptStatusEndpointTests(CodexPlanningApiWebApplicationFactory factory)
    : IClassFixture<CodexPlanningApiWebApplicationFactory>
{
    [Fact]
    public async Task Corrupt_assignment_metadata_returns_a_safe_failure_without_mutating_or_disclosing_it()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var corruptSentinel = new string('z', 129);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Projects.Add(Project.Register(projectId, "Corrupt assignment", @"C:\repos\corrupt", now));
            dbContext.Runs.Add(Run.RecordIntent(runId, projectId, 1, "Check assignment", now));
            dbContext.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('c', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, 1));
            await dbContext.SaveChangesAsync();
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE attempts SET AgentRequestedModel = {corruptSentinel} WHERE Id = {attemptId}");
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("agent_attempts.invalid_assignment", body, StringComparison.Ordinal);
        Assert.DoesNotContain(corruptSentinel, body, StringComparison.Ordinal);
        using var verifyScope = factory.Services.CreateScope();
        var verifyContext = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var persistedValue = await verifyContext.Attempts.AsNoTracking()
            .Where(attempt => attempt.Id == attemptId)
            .Select(attempt => attempt.AgentRequestedModel)
            .SingleAsync();
        Assert.Equal(corruptSentinel, persistedValue);
    }

    [Fact]
    public async Task Returns_bounded_authoritative_assignment_metadata_without_execution_secrets()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var requestedModel = "claude-model-requested";
        var observedModel = "claude-model-observed";
        var requestedEffort = "high-requested";
        var observedEffort = "medium-observed";
        var contractVersion = "claude-implementation-v1";
        var forbiddenSentinel = "must-not-be-in-response";

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = Project.Register(projectId, "Assignment status", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(runId, projectId, 1, "Implement assigned proposal", now);
            var attempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, requestedModel, requestedEffort,
                AgentPermissionProfile.WorkspaceEditOnly, contractVersion, 1);
            attempt.MarkAgentDispatched(now);
            attempt.RecordAgentObservedAssignment(observedModel, observedEffort);
            attempt.CompleteImplementation(AgentOutcome.NoChangesProduced, null, now.AddSeconds(1), processEvidence: TestProcessEvidence.CleanExit);

            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, proposalId, 0));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.True(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(proposalId, root.GetProperty("planProposalMessageId").GetGuid());
        Assert.Equal("ClaudeCode", root.GetProperty("provider").GetString());
        Assert.Equal("Implementer", root.GetProperty("role").GetString());
        Assert.Equal(requestedModel, root.GetProperty("requestedModel").GetString());
        Assert.Equal(observedModel, root.GetProperty("observedModel").GetString());
        Assert.Equal(requestedEffort, root.GetProperty("requestedEffort").GetString());
        Assert.Equal(observedEffort, root.GetProperty("observedEffort").GetString());
        Assert.Equal("WorkspaceEditOnly", root.GetProperty("permissionProfile").GetString());
        Assert.Equal(contractVersion, root.GetProperty("adapterContractVersion").GetString());
        Assert.Equal("acceptEdits", root.GetProperty("configuredPermissionMode").GetString());
        Assert.Equal("Disabled", root.GetProperty("configuredSessionPersistence").GetString());
        Assert.Equal("None", root.GetProperty("configuredPermissionPrompts").GetString());
        Assert.Equal("Ineligible", root.GetProperty("configuredResumeEligibility").GetString());

        Assert.DoesNotContain(forbiddenSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\repos\\", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        AssertNoPromptDisclosure(body);
        Assert.DoesNotContain("environment", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcript", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Shows_no_configured_permission_mode_for_a_mismatched_adapter_contract_version()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var checkpointId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var project = Project.Register(projectId, "Mismatched adapter contract", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordIntent(runId, projectId, 1, "Implement with a newer adapter contract", now);
            var attempt = Attempt.ClaimAgentImplementationWithAssignment(
                Guid.NewGuid(), runId, 1, workspaceId, checkpointId, new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, now, requestedModel: null, requestedEffort: null,
                AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v2", 1);

            dbContext.Projects.Add(project);
            dbContext.Runs.Add(run);
            dbContext.Attempts.Add(attempt);
            dbContext.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, Guid.NewGuid(), 0));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal("claude-implementation-v2", root.GetProperty("adapterContractVersion").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredPermissionMode").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredSessionPersistence").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredPermissionPrompts").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredResumeEligibility").ValueKind);
    }

    [Fact]
    public async Task Shows_no_configured_session_persistence_when_the_run_has_no_implementation_attempt_yet()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            dbContext.Projects.Add(Project.Register(projectId, "No implementation attempt yet", $@"C:\repos\{Guid.NewGuid():N}", now));
            dbContext.Runs.Add(Run.RecordIntent(runId, projectId, 1, "No implementation attempt yet", now));
            await dbContext.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/implementation");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.False(root.GetProperty("hasAttempt").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredPermissionMode").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredSessionPersistence").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredPermissionPrompts").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("configuredResumeEligibility").ValueKind);
    }

    [Fact]
    public void AssertNoPromptDisclosure_permits_only_the_two_coherent_root_level_values()
    {
        AssertNoPromptDisclosure("""{"configuredPermissionMode":"acceptEdits","configuredPermissionPrompts":"None"}""");
        AssertNoPromptDisclosure("""{"configuredPermissionMode":null,"configuredPermissionPrompts":null}""");
    }

    [Fact]
    public void AssertNoPromptDisclosure_rejects_the_safe_literal_nested_under_another_object()
    {
        // Same literal text and value, but not a root-level property — TryGetProperty on the root
        // element cannot see it, so the exception never applies here.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoPromptDisclosure(
            """{"assignment":{"configuredPermissionPrompts":"None"}}"""));
    }

    [Fact]
    public void AssertNoPromptDisclosure_rejects_a_duplicate_occurrence_of_the_safe_literal()
    {
        // A second, duplicated occurrence of the exact same root-level property/value pair means
        // the literal text appears twice in the raw body — the single-occurrence check refuses to
        // strip anything in that case, so the blanket scan still catches it.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoPromptDisclosure(
            """{"configuredPermissionPrompts":"None","configuredPermissionPrompts":"None"}"""));
    }

    [Fact]
    public void AssertNoPromptDisclosure_rejects_an_unexpected_value_for_the_same_property()
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoPromptDisclosure(
            """{"configuredPermissionPrompts":"Some"}"""));
    }

    [Fact]
    public void AssertNoPromptDisclosure_still_rejects_an_actual_leaked_prompt_alongside_the_safe_fact()
    {
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertNoPromptDisclosure(
            """{"configuredPermissionPrompts":"None","systemPrompt":"do not disclose this prompt"}"""));
    }

    // The coherent-assignment test above legitimately serializes one root-level
    // "configuredPermissionPrompts" property (a static, non-secret configuration fact — see
    // ImplementationAttemptStatusResponse.ConfiguredPermissionPrompts) whose own property name
    // contains the substring "prompt", which would otherwise trip this file's blanket
    // case-insensitive "prompt" disclosure check (guarding against a real leaked prompt/manifest
    // value). This file only ever exercises the implementation-status route, so no route
    // restriction is needed; JSON parsing still confirms the property sits directly on the root
    // object with its one coherent value before stripping only that exact, single occurrence — a
    // nested occurrence, a duplicated occurrence, an unexpected value, or any other field remains
    // fully subject to the blanket scan.
    private static void AssertNoPromptDisclosure(string body) =>
        Assert.DoesNotContain(
            "prompt", RemoveOnlyTheRootLevelConfiguredPermissionPromptsFact(body), StringComparison.OrdinalIgnoreCase);

    private static string RemoveOnlyTheRootLevelConfiguredPermissionPromptsFact(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("configuredPermissionPrompts", out var property))
        {
            return body;
        }

        var literal = property.ValueKind switch
        {
            JsonValueKind.Null => "\"configuredPermissionPrompts\":null",
            JsonValueKind.String when property.GetString() == "None" => "\"configuredPermissionPrompts\":\"None\"",
            _ => null,
        };

        return literal is not null && CountOccurrences(body, literal) == 1
            ? body.Replace(literal, "", StringComparison.Ordinal)
            : body;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }
}
