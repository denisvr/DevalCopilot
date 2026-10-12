using System.Net;
using System.Text;
using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Shared plumbing for the explicit GitHub observation scope selection (ADR-0040): a real delivered run through the production host,
/// raw reads and writes of the protected route so any body can be sent exactly as written, and counts of the only two rows the
/// operation may add.
/// </summary>
public abstract class GitHubObservationTargetTestBase : LocalDeliveryReceiptTestBase
{
    internal const string TargetRoute = "github-observation-target";

    internal const string TargetEventType = RunEventType.GitHubObservationTargetRecorded;

    internal const string DefaultOwner = "octo-cat";

    internal const string DefaultRepository = "hello.world";

    internal static readonly string[] ExcludedTables = ["github_observation_targets"];

    internal static string BodyOf(Guid operationId, string commitSha, string owner = DefaultOwner, string repository = DefaultRepository) =>
        JsonSerializer.Serialize(new { operationId, commitSha, owner, repository });

    internal static async Task<ReceiptReading> PostTargetAsync(LocalCommitHost host, Guid runId, string json)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/runs/{runId}/{TargetRoute}", content);
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    internal static async Task<ReceiptReading> GetTargetAsync(LocalCommitHost host, Guid runId)
    {
        using var client = LocalCommitLineage.AuthenticatedClient(host);
        var response = await client.GetAsync($"/api/runs/{runId}/{TargetRoute}");
        return new ReceiptReading(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Records the default selection of a delivered run and returns the exact recorded body.</summary>
    internal static async Task<string> RecordAsync(
        LocalCommitHost host, Guid runId, Guid operationId, string commitSha, string owner = DefaultOwner, string repository = DefaultRepository)
    {
        var reading = await PostTargetAsync(host, runId, BodyOf(operationId, commitSha, owner, repository));
        Assert.Equal(HttpStatusCode.OK, reading.Status);
        return reading.Body;
    }

    internal async Task<(int Targets, int Events)> CountsAsync(Guid runId)
    {
        await using var db = OpenDb();
        return (
            await db.GitHubObservationTargets.AsNoTracking().CountAsync(target => target.RunId == runId),
            await db.Events.AsNoTracking().CountAsync(runEvent => runEvent.RunId == runId && runEvent.EventType == TargetEventType));
    }

    internal async Task<string> CommitShaOfAsync(Guid operationId) => (await OperationByIdAsync(operationId)).CommitSha;

    internal async Task<GitHubObservationTarget> TargetRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.GitHubObservationTargets.AsNoTracking().SingleAsync(target => target.RunId == runId);
    }

    internal async Task<RunEvent> TargetEventRowAsync(Guid runId)
    {
        await using var db = OpenDb();
        return await db.Events.AsNoTracking().SingleAsync(runEvent => runEvent.RunId == runId && runEvent.EventType == TargetEventType);
    }

    internal Task<string> FingerprintWithoutTargetWritesAsync() => DatabaseFingerprintAsync(ExcludedTables, TargetEventType);

    internal static string[] KeysOf(JsonElement element) =>
        [.. element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];
}
