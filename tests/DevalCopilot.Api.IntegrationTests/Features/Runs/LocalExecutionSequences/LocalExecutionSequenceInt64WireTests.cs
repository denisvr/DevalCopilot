using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalExecutionSequences;

/// <summary>ADR-0060: event sequences and revisions around the signed 64-bit maximum roundtrip exactly as canonical strings. A row at
/// the maximum saturates SQLite's counters, so this class owns a private host.</summary>
public sealed class LocalExecutionSequenceInt64WireTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    [Fact]
    public async Task Event_sequences_and_revisions_roundtrip_as_canonical_strings_at_the_signed_64_bit_maximum()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new LocalExecutionSequenceMatchingEvidenceReader());
        }));
        LocalExecutionSequenceApiScene scene;
        using (var scope = host.Services.CreateScope())
        {
            scene = await LocalExecutionSequenceApiSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>());
        }

        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var route = $"/api/runs/{scene.RunId}/local-execution-sequence";
        using var availability = JsonDocument.Parse(await client.GetStringAsync($"{route}/availability/{scene.PlanId}"));
        var confirmation = availability.RootElement.GetProperty("confirmationSha256").GetString();
        var armed = await client.PostAsync(
            route,
            new StringContent(
                JsonSerializer.Serialize(new
                {
                    planProposalMessageId = scene.PlanId,
                    workspaceId = scene.WorkspaceId,
                    checkpointId = scene.CheckpointId,
                    fingerprintSha256 = LocalExecutionSequenceApiSeed.Fingerprint,
                    confirmationSha256 = confirmation,
                }),
                Encoding.UTF8,
                "application/json"));
        Assert.Equal(HttpStatusCode.OK, armed.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE events SET Sequence = 9223372036854775806 WHERE RunId = {0} AND EventType = 'local_execution_sequence.armed'", scene.RunId);
            await db.Database.ExecuteSqlRawAsync("UPDATE local_execution_sequences SET Revision = 9223372036854775807 WHERE RunId = {0}", scene.RunId);
        }

        var response = await client.GetAsync(route);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"latestEventSequence\":\"9223372036854775806\"", text, StringComparison.Ordinal);
        Assert.Contains("\"revision\":\"9223372036854775807\"", text, StringComparison.Ordinal);
    }
}
