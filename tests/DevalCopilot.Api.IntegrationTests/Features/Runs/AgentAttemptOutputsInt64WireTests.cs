using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// ADR-0054: message sequences around 2^53 and up to the signed 64-bit maximum roundtrip exactly as canonical strings, in the reference
/// list and in the selected output's content. A row at the maximum saturates SQLite's sequence counter, so this class owns a private host.
/// </summary>
public sealed class AgentAttemptOutputsInt64WireTests(ApiWebApplicationFactory factory)
    : AgentAttemptOutputsTestBase(factory), IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Int64_message_sequences_roundtrip_exactly_as_strings()
    {
        var runId = await SeedRunAsync();
        var attempt = await SeedResolverAsync(runId);
        var message = (await SeedOutputsAsync(attempt, 1))[0];

        foreach (var sequence in new[] { "9007199254740992", "9007199254740993", "9223372036854775806", "9223372036854775807" })
        {
            await ExecuteAsync($"UPDATE collaboration_messages SET Sequence = {long.Parse(sequence)} WHERE Id = {message.Id}");

            var list = await GetAsync($"/api/runs/{runId}/agent-attempts/{attempt.Id}/outputs");

            Assert.True(list.Status == System.Net.HttpStatusCode.OK, list.Text);
            Assert.Equal("Recorded", list.Body.GetProperty("status").GetString());
            Assert.Equal(sequence, list.Body.GetProperty("outputMessages")[0].GetProperty("collaborationSequence").GetString());
            Assert.Contains($"\"collaborationSequence\":\"{sequence}\"", list.Text, StringComparison.Ordinal);

            var selected = await GetAsync($"/api/runs/{runId}/agent-attempts/{attempt.Id}/outputs/{message.Id}");

            Assert.True(selected.Status == System.Net.HttpStatusCode.OK, selected.Text);
            var root = selected.Body;
            Assert.Equal("Recorded", root.GetProperty("status").GetString());
            Assert.Equal(sequence, root.GetProperty("output").GetProperty("collaborationSequence").GetString());
            Assert.Equal(sequence, root.GetProperty("message").GetProperty("sequence").GetString());
            Assert.Equal("Available", root.GetProperty("message").GetProperty("contentStatus").GetString());
            Assert.Contains($"\"collaborationSequence\":\"{sequence}\"", selected.Text, StringComparison.Ordinal);
            Assert.Contains($"\"sequence\":\"{sequence}\"", selected.Text, StringComparison.Ordinal);
        }
    }
}
