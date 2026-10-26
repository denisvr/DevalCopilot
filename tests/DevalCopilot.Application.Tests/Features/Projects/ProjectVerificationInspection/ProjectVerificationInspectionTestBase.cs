using Devalente.Shared.Results;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationHistory;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectVerificationInspection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

/// <summary>The shared plumbing of the ADR-0069 tests: a disposable migrated file-backed SQLite database per test, the feature-owned seed,
/// the real handlers over contexts of their own (each asserted to track nothing) and a way to write damaged storage through a context
/// of its own with foreign keys off, the only way to hold rows the Domain refuses to record.</summary>
public abstract class ProjectVerificationInspectionTestBase : IAsyncLifetime
{
    private readonly SqliteDatabaseFixture _fixture = new();

    protected ProjectVerificationInspectionTestBase() => Seed = new ProjectVerificationInspectionSeed(_fixture);

    internal ProjectVerificationInspectionSeed Seed { get; }

    protected SqliteDatabaseFixture Fixture => _fixture;

    public virtual Task InitializeAsync() => _fixture.InitializeAsync();

    public virtual Task DisposeAsync() => _fixture.DisposeAsync();

    protected async Task ExecuteAsync(FormattableString sql)
    {
        await using var dbContext = _fixture.CreateContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    protected async Task<Result<GetProjectVerificationHistoryQueryResult>> TryHistoryAsync(
        Guid projectId, int? before = null, params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        var result = await new GetProjectVerificationHistoryQueryHandler(dbContext).HandleAsync(
            new GetProjectVerificationHistoryQuery(projectId, before), CancellationToken.None);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        return result;
    }

    protected async Task<GetProjectVerificationHistoryQueryResult> HistoryAsync(
        Guid projectId, int? before = null, params IInterceptor[] interceptors)
    {
        var result = await TryHistoryAsync(projectId, before, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    protected async Task<Result<GetProjectVerificationInspectionQueryResult>> TryInspectAsync(
        Guid projectId, Guid executionId, params IInterceptor[] interceptors)
    {
        await using var dbContext = _fixture.CreateContext(interceptors);
        var result = await new GetProjectVerificationInspectionQueryHandler(dbContext).HandleAsync(
            new GetProjectVerificationInspectionQuery(projectId, executionId), CancellationToken.None);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        return result;
    }

    protected async Task<GetProjectVerificationInspectionQueryResult> InspectAsync(
        Guid projectId, Guid executionId, params IInterceptor[] interceptors)
    {
        var result = await TryInspectAsync(projectId, executionId, interceptors);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Errors[0].Code : null);
        return result.Value;
    }

    /// <summary>Every durable fact an inspection could disturb, as one comparable value: row counts of the recorded tables and the stored
    /// status, completion, fingerprint and artifact facts of every execution. It deliberately does not claim whole-database identity.</summary>
    protected async Task<string> ChangeFingerprintAsync()
    {
        await using var dbContext = _fixture.CreateContext();
        var projects = await dbContext.Projects.AsNoTracking().CountAsync();
        var workspaces = await dbContext.GitWorkspaces.AsNoTracking().CountAsync();
        var checkpoints = await dbContext.GitCheckpoints.AsNoTracking().CountAsync();
        var commands = await dbContext.VerificationCommands.AsNoTracking().CountAsync();
        var runs = await dbContext.Runs.AsNoTracking().CountAsync();
        var attempts = await dbContext.Attempts.AsNoTracking().CountAsync();
        var outputs = (await dbContext.VerificationOutputArtifacts.AsNoTracking().ToListAsync())
            .OrderBy(output => output.Id).Select(output => $"{output.Id}:{output.Purpose}:{output.ByteLength}:{output.ContentHash}");
        var executions = (await dbContext.VerificationExecutions.AsNoTracking().ToListAsync())
            .OrderBy(execution => execution.Id)
            .Select(execution => $"{execution.Id}:{execution.Status}:{execution.Outcome}:{execution.ExitCode}:{execution.CompletionFingerprintSha256}"
                + $":{execution.DispatchedAtUtc:O}:{execution.CompletedAtUtc:O}");
        return $"{projects}|{workspaces}|{checkpoints}|{commands}|{runs}|{attempts}|{string.Join(',', outputs)}|{string.Join(',', executions)}";
    }
}
