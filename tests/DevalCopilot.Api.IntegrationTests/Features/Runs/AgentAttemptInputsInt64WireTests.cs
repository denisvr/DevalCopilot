using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0051 and ADR-0053: referenced message sequences around 2^53 and up to the signed 64-bit maximum roundtrip exactly as canonical
/// strings, in the reference list and in the selected input's content. A row at
/// the maximum saturates SQLite's sequence counter, so this class owns a private host.
/// </summary>
public sealed class AgentAttemptInputsInt64WireTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Int64_referenced_message_sequences_roundtrip_exactly_as_strings()
    {
        var runId = await SeedRunAsync();
        var attempt = await WithDbAsync(async db =>
        {
            var claimed = Attempt.ClaimAgentChallengeResolution(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, DateTimeOffset.UtcNow, 1);
            db.Attempts.Add(claimed);
            await db.SaveChangesAsync();
            return claimed;
        });
        var message = (await AppendAsync(runId, 1))[0];
        await WithDbAsync(async db =>
        {
            db.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attempt.Id, message.Id, 0));
            return await db.SaveChangesAsync();
        });

        foreach (var sequence in new[] { "9007199254740992", "9007199254740993", "9223372036854775806", "9223372036854775807" })
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = {long.Parse(sequence)} WHERE Id = {message.Id}");
            using var client = AuthenticatedClient();

            var response = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attempt.Id}/inputs");
            var text = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode, text);
            var root = JsonDocument.Parse(text).RootElement;
            Assert.Equal("Recorded", root.GetProperty("status").GetString());
            Assert.Equal(sequence, root.GetProperty("inputMessages")[0].GetProperty("collaborationSequence").GetString());
            Assert.Contains($"\"collaborationSequence\":\"{sequence}\"", text, StringComparison.Ordinal);

            // ADR-0053: the selected input's content carries the same exact value as both its input and message sequence.
            var selected = await client.GetAsync($"/api/runs/{runId}/agent-attempts/{attempt.Id}/inputs/{message.Id}");
            var selectedText = await selected.Content.ReadAsStringAsync();

            Assert.True(selected.IsSuccessStatusCode, selectedText);
            var selectedRoot = JsonDocument.Parse(selectedText).RootElement;
            Assert.Equal("Recorded", selectedRoot.GetProperty("status").GetString());
            Assert.Equal(sequence, selectedRoot.GetProperty("input").GetProperty("collaborationSequence").GetString());
            Assert.Equal(sequence, selectedRoot.GetProperty("message").GetProperty("sequence").GetString());
            Assert.Equal("Available", selectedRoot.GetProperty("message").GetProperty("contentStatus").GetString());
            Assert.Contains($"\"collaborationSequence\":\"{sequence}\"", selectedText, StringComparison.Ordinal);
            Assert.Contains($"\"sequence\":\"{sequence}\"", selectedText, StringComparison.Ordinal);
        }
    }
}
