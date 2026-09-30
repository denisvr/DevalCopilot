using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Queries.GetImplementationAttemptStatus;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetImplementationAttemptStatusQueryHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);
    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Theory]
    [InlineData("AgentRequestedModel")]
    [InlineData("AgentAdapterContractVersion")]
    public async Task HandleAsync_fails_closed_for_corrupt_assignment_without_mutating_it(string column)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var secretSentinel = new string('x', 129);
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Status assignment", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect assignment", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('a', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));
            await seed.SaveChangesAsync();
            _ = column switch
            {
                "AgentRequestedModel" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentRequestedModel = {secretSentinel} WHERE Id = {attemptId}"),
                "AgentAdapterContractVersion" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentAdapterContractVersion = {secretSentinel} WHERE Id = {attemptId}"),
                _ => throw new InvalidOperationException("Unexpected test column."),
            };
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.invalid_assignment", error.Code);
        Assert.DoesNotContain(secretSentinel, error.Description, StringComparison.Ordinal);
        var persistedValue = column switch
        {
            "AgentRequestedModel" => await context.Attempts.AsNoTracking()
                .Where(attempt => attempt.Id == attemptId).Select(attempt => attempt.AgentRequestedModel).SingleAsync(),
            "AgentAdapterContractVersion" => await context.Attempts.AsNoTracking()
                .Where(attempt => attempt.Id == attemptId).Select(attempt => attempt.AgentAdapterContractVersion).SingleAsync(),
            _ => throw new InvalidOperationException("Unexpected test column."),
        };
        Assert.Equal(secretSentinel, persistedValue);
    }

    [Theory]
    [InlineData("AgentProvider", "InvalidProviderSentinel")]
    [InlineData("AgentPermissionProfile", "InvalidPermissionSentinel")]
    public async Task HandleAsync_fails_closed_for_unparseable_persisted_enum_without_mutating_it(string column, string sentinel)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Corrupt enum assignment", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect corrupted assignment", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('d', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));
            await seed.SaveChangesAsync();
            _ = column switch
            {
                "AgentProvider" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentProvider = {sentinel} WHERE Id = {attemptId}"),
                "AgentPermissionProfile" => await seed.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE attempts SET AgentPermissionProfile = {sentinel} WHERE Id = {attemptId}"),
                _ => throw new InvalidOperationException("Unexpected test column."),
            };
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.invalid_assignment", error.Code);
        Assert.DoesNotContain(sentinel, error.Description, StringComparison.Ordinal);
        var persistedValue = column switch
        {
            "AgentProvider" => await context.Database
                .SqlQueryRaw<string>("SELECT AgentProvider AS Value FROM attempts WHERE Id = {0}", attemptId)
                .SingleAsync(),
            "AgentPermissionProfile" => await context.Database
                .SqlQueryRaw<string>("SELECT AgentPermissionProfile AS Value FROM attempts WHERE Id = {0}", attemptId)
                .SingleAsync(),
            _ => throw new InvalidOperationException("Unexpected test column."),
        };
        Assert.Equal(sentinel, persistedValue);
    }

    [Fact]
    public async Task HandleAsync_projects_valid_historical_nullable_assignment_as_unknown_and_null()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Historical assignment", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect historical assignment", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('b', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));
            seed.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, Guid.NewGuid(), 0));
            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE attempts SET AgentRequestedModel = NULL, AgentObservedModel = NULL,
                    AgentRequestedEffort = NULL, AgentObservedEffort = NULL,
                    AgentPermissionProfile = NULL, AgentAdapterContractVersion = NULL
                WHERE Id = {attemptId}
                """);
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AgentPermissionProfile.Unknown, result.Value.Assignment!.PermissionProfile);
        Assert.Null(result.Value.Assignment.RequestedModel);
        Assert.Null(result.Value.Assignment.ObservedModel);
        Assert.Null(result.Value.Assignment.RequestedEffort);
        Assert.Null(result.Value.Assignment.ObservedEffort);
        Assert.Null(result.Value.Assignment.AdapterContractVersion);
        Assert.Null(result.Value.ConfiguredPermissionMode);
        Assert.Null(result.Value.ConfiguredSessionPersistence);
        Assert.Null(result.Value.ConfiguredPermissionPrompts);
        Assert.Null(result.Value.ConfiguredResumeEligibility);
        Assert.Null(result.Value.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_the_configured_permission_mode_for_a_coherent_default_assignment()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Configured permission mode", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect configured permission mode", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementation(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('e', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, 1));
            seed.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, Guid.NewGuid(), 0));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acceptEdits", result.Value.ConfiguredPermissionMode);
        Assert.Equal("Disabled", result.Value.ConfiguredSessionPersistence);
        Assert.Equal("None", result.Value.ConfiguredPermissionPrompts);
        Assert.Equal("Ineligible", result.Value.ConfiguredResumeEligibility);
        Assert.Equal("Read,Edit,Write,Glob,Grep", result.Value.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_permission_mode_for_a_mismatched_adapter_contract_version()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Mismatched adapter contract", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect mismatched adapter contract", Now));
            seed.Attempts.Add(Attempt.ClaimAgentImplementationWithAssignment(
                attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('f', 64), Guid.NewGuid(),
                TimeSpan.FromMinutes(20), 65536, 131072, Now, requestedModel: null, requestedEffort: null,
                AgentPermissionProfile.WorkspaceEditOnly, "claude-implementation-v3", 1));
            seed.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, Guid.NewGuid(), 0));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ConfiguredPermissionMode);
        Assert.Null(result.Value.ConfiguredSessionPersistence);
        Assert.Null(result.Value.ConfiguredPermissionPrompts);
        Assert.Null(result.Value.ConfiguredResumeEligibility);
        Assert.Null(result.Value.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_session_persistence_for_no_attempt()
    {
        var runId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "No implementation attempt yet", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "No implementation attempt yet", Now));
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.HasAttempt);
        Assert.Null(result.Value.ConfiguredPermissionMode);
        Assert.Null(result.Value.ConfiguredSessionPersistence);
        Assert.Null(result.Value.ConfiguredPermissionPrompts);
        Assert.Null(result.Value.ConfiguredResumeEligibility);
        Assert.Null(result.Value.ConfiguredBuiltInTools);
    }

    private async Task<(Guid RunId, Guid AttemptId)> SeedTurnLimitAttemptAsync(
        string version, int? attemptLimit, int? runLimit = null, bool legacyFactory = false)
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "Turn limit status", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "Inspect turn limit", Now));
            var requestedMaxTurns = version == ClaudeMutationAdapterContract.ImplementationV2 && ClaudeMutationTurnLimit.IsValid(attemptLimit) ? attemptLimit : null;
            seed.Attempts.Add(legacyFactory
                ? Attempt.ClaimAgentImplementation(
                    attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('e', 64), Guid.NewGuid(),
                    TimeSpan.FromMinutes(20), 65536, 131072, Now, 1)
                : Attempt.ClaimAgentImplementationWithAssignment(
                    attemptId, runId, 1, Guid.NewGuid(), Guid.NewGuid(), new string('e', 64), Guid.NewGuid(),
                    TimeSpan.FromMinutes(20), 65536, 131072, Now, requestedModel: null, requestedEffort: null,
                    AgentPermissionProfile.WorkspaceEditOnly, version, 1, requestedMaxTurns));
            seed.AttemptInputMessages.Add(AttemptInputMessage.Record(Guid.NewGuid(), attemptId, Guid.NewGuid(), 0));
            await seed.SaveChangesAsync();
        }

        await ClaudeMutationTurnLimitTestSupport.SetRawAttemptLimitAsync(_fixture, attemptId, attemptLimit);
        await ClaudeMutationTurnLimitTestSupport.SetRawRunLimitAsync(_fixture, runId, runLimit);
        return (runId, attemptId);
    }

    private async Task<ImplementationAttemptStatusQueryResult> ReadStatusAsync(Guid runId)
    {
        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);
        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Code)));
        return result.Value;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(100)]
    public async Task HandleAsync_reports_a_requested_turn_limit_for_a_current_v2_attempt(int limit)
    {
        var (runId, _) = await SeedTurnLimitAttemptAsync(ClaudeMutationAdapterContract.ImplementationV2, limit, runLimit: 55);

        var status = await ReadStatusAsync(runId);

        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Requested, limit), status.AttemptTurnLimit);
        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Requested, 55), status.RunTurnLimitRequest);
        Assert.Equal("acceptEdits", status.ConfiguredPermissionMode);
        Assert.Equal("Read,Edit,Write,Glob,Grep", status.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_not_requested_for_a_v2_attempt_without_a_limit()
    {
        var (runId, _) = await SeedTurnLimitAttemptAsync(ClaudeMutationAdapterContract.ImplementationV2, attemptLimit: null);

        var status = await ReadStatusAsync(runId);

        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.NotRequested, null), status.AttemptTurnLimit);
        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.NotRequested, null), status.RunTurnLimitRequest);
        Assert.Equal("acceptEdits", status.ConfiguredPermissionMode);
    }

    [Fact]
    public async Task HandleAsync_reports_not_recorded_for_a_legacy_v1_attempt_and_still_populates_configured_facts()
    {
        var (runId, _) = await SeedTurnLimitAttemptAsync(
            ClaudeMutationAdapterContract.ImplementationV1, attemptLimit: null, legacyFactory: true);

        var status = await ReadStatusAsync(runId);

        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.NotRecorded, null), status.AttemptTurnLimit);
        Assert.Equal("acceptEdits", status.ConfiguredPermissionMode);
        Assert.Equal("Disabled", status.ConfiguredSessionPersistence);
        Assert.Equal("None", status.ConfiguredPermissionPrompts);
        Assert.Equal("Ineligible", status.ConfiguredResumeEligibility);
        Assert.Equal("Read,Edit,Write,Glob,Grep", status.ConfiguredBuiltInTools);
    }

    [Fact]
    public async Task HandleAsync_reports_unknown_for_a_limit_stored_beside_a_v1_attempt()
    {
        var (runId, _) = await SeedTurnLimitAttemptAsync(
            ClaudeMutationAdapterContract.ImplementationV1, attemptLimit: 5, legacyFactory: true);

        var status = await ReadStatusAsync(runId);

        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Unknown, null), status.AttemptTurnLimit);
    }

    [Fact]
    public async Task HandleAsync_reports_no_configured_facts_for_an_unknown_version_but_unknown_limit_evidence()
    {
        var (runId, _) = await SeedTurnLimitAttemptAsync("claude-implementation-v3", attemptLimit: null);

        var status = await ReadStatusAsync(runId);

        Assert.Null(status.ConfiguredPermissionMode);
        Assert.Null(status.ConfiguredBuiltInTools);
        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Unknown, null), status.AttemptTurnLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task HandleAsync_fails_closed_for_a_malformed_out_of_range_attempt_limit(int stored)
    {
        var (runId, attemptId) = await SeedTurnLimitAttemptAsync(ClaudeMutationAdapterContract.ImplementationV2, stored);

        await using var context = _fixture.CreateContext();
        var result = await new GetImplementationAttemptStatusQueryHandler(context)
            .HandleAsync(new GetImplementationAttemptStatusQuery(runId), CancellationToken.None);

        Assert.True(result.IsFailure);
        var error = Assert.Single(result.Errors);
        Assert.Equal("agent_attempts.invalid_assignment", error.Code);
        Assert.DoesNotContain(stored.ToString(), error.Description, StringComparison.Ordinal);
        Assert.Equal(stored.ToString(), await ClaudeMutationTurnLimitTestSupport.ReadAttemptRawAsync(_fixture, attemptId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task HandleAsync_reports_the_run_request_as_unknown_when_the_stored_value_is_out_of_range(int stored)
    {
        var (runId, _) = await SeedTurnLimitAttemptAsync(
            ClaudeMutationAdapterContract.ImplementationV2, attemptLimit: 5, runLimit: stored);

        var status = await ReadStatusAsync(runId);

        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Unknown, null), status.RunTurnLimitRequest);
        Assert.Equal(new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Requested, 5), status.AttemptTurnLimit);
    }

    [Fact]
    public async Task HandleAsync_still_reports_the_run_request_when_there_is_no_attempt()
    {
        var runId = Guid.NewGuid();
        await using (var seed = _fixture.CreateContext())
        {
            var project = Project.Register(Guid.NewGuid(), "No attempt turn limit", @"C:\repo", Now);
            seed.Projects.Add(project);
            seed.Runs.Add(Run.RecordIntent(runId, project.Id, 1, "No attempt", Now));
            await seed.SaveChangesAsync();
        }

        var withoutRequest = await ReadStatusAsync(runId);
        Assert.False(withoutRequest.HasAttempt);
        Assert.Null(withoutRequest.AttemptTurnLimit);
        Assert.Equal(
            new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.NotRequested, null), withoutRequest.RunTurnLimitRequest);

        await ClaudeMutationTurnLimitTestSupport.SetRawRunLimitAsync(_fixture, runId, 21);
        var withRequest = await ReadStatusAsync(runId);
        Assert.Equal(
            new ClaudeMutationTurnLimitFact(ClaudeMutationTurnLimitEvidence.Requested, 21), withRequest.RunTurnLimitRequest);
    }
}
