using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.PlanningSequences;

/// <summary>ADR-0055: event sequences and revisions around the signed 64-bit maximum roundtrip exactly as canonical strings. A row at
/// the maximum saturates SQLite's counters, so this class owns a private host.</summary>
public sealed class PlanningSequenceInt64WireTests(ApiWebApplicationFactory factory) : IClassFixture<ApiWebApplicationFactory>
{
    private static readonly string Fingerprint = new('a', 64);

    private sealed class MatchingEvidenceReader : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null));
    }

    [Fact]
    public async Task Event_sequences_and_revisions_roundtrip_as_canonical_strings_at_the_signed_64_bit_maximum()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGitWorkspaceEvidenceReader>();
            services.AddSingleton<IGitWorkspaceEvidenceReader>(new MatchingEvidenceReader());
        }));
        Guid runId;
        Guid workspaceId;
        Guid checkpointId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            var now = DateTimeOffset.UtcNow.AddMinutes(-10);
            var project = Project.Register(Guid.NewGuid(), "Wire", $@"C:\repos\{Guid.NewGuid():N}", now);
            var run = Run.RecordClassifiedIntent(Guid.NewGuid(), project.Id, project.ReserveExecutionNumber(), RunExecutionMode.ManualAgent, "Plan", now);
            var workspace = GitWorkspace.Prepare(Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
            workspace.MarkReady();
            var checkpoint = GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, workspace.ReserveCheckpointNumber(), now, new string('a', 40), Fingerprint, []);
            db.Projects.Add(project);
            db.Runs.Add(run);
            db.GitWorkspaces.Add(workspace);
            db.GitCheckpoints.Add(checkpoint);
            db.RepositoryMutationLeases.Add(RepositoryMutationLease.Acquire(
                Guid.NewGuid(), project.Id, workspace.Id, BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0), Guid.NewGuid().ToByteArray(), now));
            await db.SaveChangesAsync();
            (runId, workspaceId, checkpointId) = (run.Id, workspace.Id, checkpoint.Id);
        }

        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);
        var armed = await client.PostAsync(
            $"/api/runs/{runId}/planning-sequence",
            new StringContent(JsonSerializer.Serialize(new { workspaceId, checkpointId, fingerprintSha256 = Fingerprint }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, armed.StatusCode);

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE events SET Sequence = 9223372036854775806 WHERE RunId = {0} AND EventType = 'planning_sequence.armed'", runId);
            await db.Database.ExecuteSqlRawAsync("UPDATE planning_sequences SET Revision = 9223372036854775807 WHERE RunId = {0}", runId);
        }

        var response = await client.GetAsync($"/api/runs/{runId}/planning-sequence");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"latestEventSequence\":\"9223372036854775806\"", text, StringComparison.Ordinal);
        Assert.Contains("\"revision\":\"9223372036854775807\"", text, StringComparison.Ordinal);
    }
}
