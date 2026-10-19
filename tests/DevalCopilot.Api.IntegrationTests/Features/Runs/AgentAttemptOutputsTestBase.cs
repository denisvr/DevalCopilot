using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>Shared seeding for the ADR-0054 output routes: a real, dispatched Agent attempt and the messages that record it as their own
/// attempt, through the production <see cref="CollaborationMessage.RecordAgent"/> factory.</summary>
public abstract class AgentAttemptOutputsTestBase(ApiWebApplicationFactory factory) : CollaborationHistoryTestBase(factory)
{
    protected static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    protected Task<Attempt> SeedResolverAsync(Guid runId, int number = 1, bool dispatched = true) => WithDbAsync(async db =>
    {
        var attempt = Attempt.ClaimAgentChallengeResolution(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, number);
        if (dispatched)
        {
            attempt.MarkAgentDispatched(Now.AddSeconds(1));
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    });

    protected Task<Attempt> SeedPlannerAsync(Guid runId, int number = 1, bool dispatched = true) => WithDbAsync(async db =>
    {
        var attempt = Attempt.ClaimAgent(
            Guid.NewGuid(), runId, number, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
            TimeSpan.FromMinutes(10), 1024, 2048, Now, number);
        if (dispatched)
        {
            attempt.MarkAgentDispatched(Now.AddSeconds(1));
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    });

    /// <summary>Records messages of the attempt, cycling through the types a Resolver may author in the order Proposal, Question,
    /// Decision, Escalation, one save each so their sequences ascend with their index.</summary>
    protected Task<List<CollaborationMessage>> SeedOutputsAsync(Attempt attempt, int count) => WithDbAsync(async db =>
    {
        CollaborationMessageType[] types = attempt.AgentRole == AgentRole.Planner
            ? [CollaborationMessageType.Proposal, CollaborationMessageType.Question, CollaborationMessageType.Escalation]
            : [CollaborationMessageType.Proposal, CollaborationMessageType.Question, CollaborationMessageType.Decision, CollaborationMessageType.Escalation];
        var messages = new List<CollaborationMessage>();
        for (var index = 0; index < count; index++)
        {
            var type = types[index % types.Length];
            var message = CollaborationMessage.RecordAgent(
                attempt,
                Guid.NewGuid(),
                ParticipantIdentity.ForOrchestrator(),
                type,
                type == CollaborationMessageType.Proposal ? null : Guid.NewGuid(),
                $"Recorded {type} summary {index}.",
                ContentJson(type),
                Now.AddSeconds(index + 2).AddTicks(1234567));
            db.CollaborationMessages.Add(message);
            await db.SaveChangesAsync();
            messages.Add(message);
        }

        return messages;
    });

    protected static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    protected static string Normalize(string text)
    {
        var body = JsonNode.Parse(text)!.AsObject();
        body.Remove("traceId");
        body.Remove("instance");
        return body.ToJsonString();
    }

    protected async Task<(HttpStatusCode Status, JsonElement Body, string Text)> GetAsync(string route)
    {
        using var client = AuthenticatedClient();
        var response = await client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone(), text);
    }

    /// <summary>The fixed not-found Problem Details tuple, identical for every case and repeating no identity.</summary>
    protected static void AssertFixedNotFound(
        IReadOnlyCollection<(HttpStatusCode Status, JsonElement Body, string Text)> responses, string code, params Guid[] hidden)
    {
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.Status);
            Assert.Equal("urn:devalente:problem:not-found", response.Body.GetProperty("type").GetString());
            Assert.Equal(404, response.Body.GetProperty("status").GetInt32());
            var error = Assert.Single(response.Body.GetProperty("errors").EnumerateArray());
            Assert.Equal(code, error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(response.Body.GetProperty("traceId").GetString()));
            var body = Normalize(response.Text);
            foreach (var value in hidden)
            {
                Assert.DoesNotContain(value.ToString(), body, StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Single(responses.Select(response => Normalize(response.Text)).Distinct());
    }
}
