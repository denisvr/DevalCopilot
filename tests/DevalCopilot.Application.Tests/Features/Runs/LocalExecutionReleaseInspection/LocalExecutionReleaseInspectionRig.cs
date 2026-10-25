using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Runs.Queries.GetLocalExecutionReleaseInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionInspection;
using DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionReleaseInspection;

/// <summary>
/// ADR-0067 test support: the production recorded release inspection handler over a context of its own, with the proof that it left no
/// tracked entity, and one comparable fingerprint of every durable fact the read could disturb (including the three append-only release
/// facts and their events). Nothing here writes through the handler; the scene alone changes the world.
/// </summary>
internal static class LocalExecutionReleaseInspectionRig
{
    public static async Task<Result<GetLocalExecutionReleaseInspectionQueryResult>> TryInspectAsync(
        SqliteDatabaseFixture fixture, Guid runId, Guid stepId, params IInterceptor[] interceptors)
    {
        await using var context = fixture.CreateContext(interceptors);
        var result = await new GetLocalExecutionReleaseInspectionQueryHandler(context).HandleAsync(
            new GetLocalExecutionReleaseInspectionQuery(runId, stepId), CancellationToken.None);
        Assert.Empty(context.ChangeTracker.Entries());
        return result;
    }

    public static async Task<GetLocalExecutionReleaseInspectionQueryResult> InspectAsync(
        SqliteDatabaseFixture fixture, Guid runId, Guid stepId, params IInterceptor[] interceptors)
    {
        var result = await TryInspectAsync(fixture, runId, stepId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        Assert.Equal(runId, result.Value.RunId);
        Assert.Equal(stepId, result.Value.StepId);
        return result.Value;
    }

    /// <summary>Every durable fact the inspection could disturb: the general inspection fingerprint plus the witnesses, observations,
    /// releases and Human release events.</summary>
    public static async Task<string> FingerprintAsync(SqliteDatabaseFixture fixture, LocalExecutionSequenceTestScene scene)
    {
        var general = await new LocalExecutionInspectionHarness(scene).ChangeFingerprintAsync();
        await using var context = fixture.CreateContext();
        var witnesses = await context.LocalExecutionDispatchWitnesses.AsNoTracking().CountAsync();
        var observations = await context.LocalExecutionTerminationObservations.AsNoTracking().CountAsync();
        var releases = await context.LocalExecutionOwnershipReleases.AsNoTracking().CountAsync();
        var events = await context.Events.AsNoTracking().CountAsync(candidate => candidate.EventType == RunEventType.LocalExecutionOwnershipReleased);
        return $"{general}|{witnesses}|{observations}|{releases}|{events}";
    }

    /// <summary>The read is exactly one SELECT, never selects the private host launch identity or nonce, and the statement log saw no
    /// other command.</summary>
    public static void AssertOneReadOnlyStatement(StatementLog log)
    {
        var statement = Assert.Single(log.Statements);
        Assert.StartsWith("SELECT", statement.TrimStart(), StringComparison.Ordinal);
        log.AssertNeverSelects("\"Nonce\"", "\"HostLaunchId\"");
    }
}
