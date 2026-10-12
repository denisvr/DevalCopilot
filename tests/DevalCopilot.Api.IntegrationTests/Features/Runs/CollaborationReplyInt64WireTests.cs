using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0036: the source and parent sequences around 2^53 and up to the signed 64-bit maximum roundtrip exactly, and the strictly-earlier
/// rule is decided on the exact integers. A row at the maximum saturates SQLite's sequence counter, so this class owns a private host.
/// </summary>
public sealed class CollaborationReplyInt64WireTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Int64_source_and_parent_sequences_roundtrip_exactly_and_forward_by_one_above_two_to_the_fifty_third_is_refused()
    {
        var runId = await SeedRunAsync();
        var proposals = await AppendAsync(runId, 1);
        var accepts = await WithDbAsync(async db =>
        {
            var list = new List<CollaborationMessage>();
            for (var index = 0; index < 3; index++)
            {
                var message = CollaborationMessage.Record(
                    Guid.NewGuid(), runId, null, CollaborationMessage.ProtocolVersionOne,
                    ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForOrchestrator(),
                    CollaborationMessageType.Acceptance, proposals[0].Id, $"Recorded Acceptance summary {index}.",
                    ContentJson(CollaborationMessageType.Acceptance), CollaborationMessageProvenance.ProviderObserved,
                    new DateTimeOffset(2026, 10, 9, 11, 0, 0, TimeSpan.Zero).AddSeconds(index));
                db.CollaborationMessages.Add(message);
                await db.SaveChangesAsync();
                list.Add(message);
            }

            return list;
        });

        async Task<JsonElement> ReplyAsync(Guid id)
        {
            using var client = AuthenticatedClient();
            var response = await client.GetAsync($"{Route(runId)}/{id}/reply");
            Assert.True(response.IsSuccessStatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 9007199254740992 WHERE Id = {proposals[0].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 9007199254740993 WHERE Id = {accepts[0].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 9223372036854775807 WHERE Id = {accepts[1].Id}");

        var near = await ReplyAsync(accepts[0].Id);
        Assert.Equal("9007199254740993", near.GetProperty("sourceSequence").GetString());
        Assert.Equal("Available", near.GetProperty("status").GetString());
        Assert.Equal("9007199254740992", near.GetProperty("parent").GetProperty("sequence").GetString());

        var max = await ReplyAsync(accepts[1].Id);
        Assert.Equal("9223372036854775807", max.GetProperty("sourceSequence").GetString());
        Assert.Equal("9007199254740992", max.GetProperty("parent").GetProperty("sequence").GetString());
        Assert.Contains("\"sourceSequence\":\"9223372036854775807\"", max.GetRawText(), StringComparison.Ordinal);

        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 9007199254740995 WHERE Id = {proposals[0].Id}");
        await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = 9007199254740994 WHERE Id = {accepts[2].Id}");
        var forward = await ReplyAsync(accepts[2].Id);
        Assert.Equal("Unavailable", forward.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, forward.GetProperty("parent").ValueKind);
    }
}
