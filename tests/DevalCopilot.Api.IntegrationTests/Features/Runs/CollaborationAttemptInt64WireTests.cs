using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0037: source sequences around 2^53 and up to the signed 64-bit maximum roundtrip exactly as canonical strings. A row at the maximum
/// saturates SQLite's sequence counter, so this class owns a private host.
/// </summary>
public sealed class CollaborationAttemptInt64WireTests(ApiWebApplicationFactory factory)
    : CollaborationHistoryTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Int64_source_sequences_roundtrip_exactly_as_strings()
    {
        var runId = await SeedRunAsync();
        var attempt = await WithDbAsync(async db =>
        {
            var claimed = Attempt.ClaimAgent(
                Guid.NewGuid(), runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(10), 1024, 2048, DateTimeOffset.UtcNow, 1);
            db.Attempts.Add(claimed);
            await db.SaveChangesAsync();
            return claimed;
        });
        var message = await WithDbAsync(async db =>
        {
            var recorded = CollaborationMessage.Record(
                Guid.NewGuid(), runId, attempt.Id, CollaborationMessage.ProtocolVersionOne,
                ParticipantIdentity.ForAgent(AgentRole.Planner, AgentProvider.Codex), ParticipantIdentity.ForOrchestrator(),
                CollaborationMessageType.Proposal, null, "Recorded Proposal summary.", ContentJson(CollaborationMessageType.Proposal),
                CollaborationMessageProvenance.ProviderObserved, DateTimeOffset.UtcNow);
            db.CollaborationMessages.Add(recorded);
            await db.SaveChangesAsync();
            return recorded;
        });

        foreach (var sequence in new[] { "9007199254740992", "9007199254740993", "9223372036854775806", "9223372036854775807" })
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = {long.Parse(sequence)} WHERE Id = {message.Id}");
            using var client = AuthenticatedClient();

            var response = await client.GetAsync($"{Route(runId)}/{message.Id}/attempt");
            var text = await response.Content.ReadAsStringAsync();

            Assert.True(response.IsSuccessStatusCode, text);
            Assert.Equal(sequence, JsonDocument.Parse(text).RootElement.GetProperty("sourceSequence").GetString());
            Assert.Contains($"\"sourceSequence\":\"{sequence}\"", text, StringComparison.Ordinal);
            Assert.Equal("Available", JsonDocument.Parse(text).RootElement.GetProperty("status").GetString());
        }
    }
}
