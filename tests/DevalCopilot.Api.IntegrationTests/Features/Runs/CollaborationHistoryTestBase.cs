using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Shared seeding and reading for the ADR-0035 collaboration-history route tests. Rows are seeded through the Domain
/// factories (a message is recorded content, not authority); only deliberately unrecognized or defective stored values are written as
/// SQL.</summary>
public abstract class CollaborationHistoryTestBase(ApiWebApplicationFactory factory)
{
    protected const string Secret = "SECRET-SENTINEL-6e1b";

    protected ApiWebApplicationFactory Factory { get; } = factory;

    protected static readonly IReadOnlyDictionary<CollaborationMessageType, string[]> FieldNames =
        new Dictionary<CollaborationMessageType, string[]>
        {
            [CollaborationMessageType.Proposal] = ["scope", "implementationSteps", "risks", "verificationPlan", "escalationPoints"],
            [CollaborationMessageType.Acceptance] = ["rationale"],
            [CollaborationMessageType.Challenge] = ["disputedItem", "materialImpact", "reasoning", "alternativeOrQuestion"],
            [CollaborationMessageType.Question] = ["question", "why"],
            [CollaborationMessageType.Decision] = ["resolution", "rationale", "resultingPlanChanges", "nextAction"],
            [CollaborationMessageType.ExecutionReport] = ["completedWork", "verification"],
            [CollaborationMessageType.ReviewFinding] = ["severity", "category", "evidence", "requiredChange"],
            [CollaborationMessageType.RevisionResponse] = ["disposition", "evidence", "resultingSourceChanges"],
            [CollaborationMessageType.Escalation] = ["unresolvedDecision", "options", "consequences", "evidence", "recommendedChoice"],
            [CollaborationMessageType.ReviewApproval] = ["rationale", "residualRisks"],
            [CollaborationMessageType.HumanInstruction] = ["instruction", "rationale"],
        };

    protected static string Value(string name) => $"Recorded {name} text.";

    protected static string ContentJson(CollaborationMessageType type) =>
        JsonSerializer.Serialize(FieldNames[type].ToDictionary(name => name, Value));

    protected HttpClient AuthenticatedClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        return client;
    }

    protected static string Route(Guid runId, string? query = null) =>
        $"/api/runs/{runId}/collaboration-history{(query is null ? string.Empty : "?" + query)}";

    protected async Task<T> WithDbAsync<T>(Func<DevalCopilotDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
    }

    protected Task<Guid> SeedRunAsync() => WithDbAsync(async db =>
    {
        var project = Project.Register(Guid.NewGuid(), "Collaboration history", $@"C:\repos\{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        db.Projects.Add(project);
        var run = Run.RecordClassifiedIntent(
            Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Objective", DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    });

    /// <summary>Appends messages through the Domain factory, one save each, and returns them in order.</summary>
    protected Task<List<CollaborationMessage>> AppendAsync(
        Guid runId, int count, CollaborationMessageType type = CollaborationMessageType.Proposal) => WithDbAsync(async db =>
    {
        var messages = new List<CollaborationMessage>();
        var human = type == CollaborationMessageType.HumanInstruction;
        for (var index = 0; index < count; index++)
        {
            var message = CollaborationMessage.Record(
                Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
                human ? ParticipantIdentity.ForHuman() : ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex),
                ParticipantIdentity.ForOrchestrator(), type, type == CollaborationMessageType.Proposal ? null : Guid.NewGuid(),
                $"Recorded {type} summary {index}.", ContentJson(type),
                human ? CollaborationMessageProvenance.HumanSubmitted : CollaborationMessageProvenance.ProviderObserved,
                new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero).AddSeconds(index));
            db.CollaborationMessages.Add(message);
            await db.SaveChangesAsync();
            messages.Add(message);
        }

        return messages;
    });

    protected Task ExecuteAsync(FormattableString sql) => WithDbAsync(async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));

    protected async Task<(HttpStatusCode Status, JsonElement Body, string Text)> ReadAsync(Guid runId, string? query = null)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(Route(runId, query));
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    protected static string[] Sequences(JsonElement page) =>
        page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("sequence").GetString()!).ToArray();
}
