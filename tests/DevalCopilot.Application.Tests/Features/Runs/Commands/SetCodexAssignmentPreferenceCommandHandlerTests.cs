using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetCodexModelCatalog;
using DevalCopilot.Application.Features.Runs.Commands.SetCodexAssignmentPreference;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.Commands;

/// <summary>
/// Owns a fresh database per test method rather than a shared <see cref="IClassFixture{T}"/>,
/// mirroring <c>GetCodexModelCatalogQueryHandlerTests</c>: <see cref="HostCapabilitySnapshot"/> is
/// keyed one row per <see cref="Capability"/> across the whole host, so a shared database would
/// collide across test methods that each seed a <see cref="Capability.CodexCli"/> snapshot.
/// </summary>
public sealed class SetCodexAssignmentPreferenceCommandHandlerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class FakeCodexModelCatalogAdapter : ICodexModelCatalogAdapter
    {
        public CodexModelCatalogObservation Observation { get; set; } = CodexModelCatalogObservation.Unknown;
        public int CallCount { get; private set; }

        public Task<CodexModelCatalogObservation> ObserveAsync(
            string executablePath, string? scriptPath, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Observation);
        }
    }

    private static CodexModelCatalogObservation ObservedCatalog() => new(
        true,
        Now,
        [
            new CodexModelCatalogEntry("gpt-6-sol", "GPT-6 Sol", ["medium", "high"], "medium"),
            new CodexModelCatalogEntry("gpt-6-mini", "GPT-6 Mini", ["low"], null),
        ]);

    private async Task<Run> SeedVettedCodexTargetAndRunAsync(DevalCopilot.Application.Data.IDevalCopilotDbContext dbContext)
    {
        var snapshot = HostCapabilitySnapshot.Seed(Capability.CodexCli, Now);
        snapshot.MarkDispatched(Now);
        snapshot.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", Now, Now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(snapshot);

        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    [Fact]
    public async Task HandleAsync_returns_not_found_for_a_missing_run()
    {
        await using var dbContext = _fixture.CreateContext();
        var adapter = new FakeCodexModelCatalogAdapter();
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(Guid.NewGuid(), null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CodexAssignmentPreferenceErrors.RunNotFoundCode, result.Errors[0].Code);
        Assert.Equal(0, adapter.CallCount);
    }

    [Fact]
    public async Task HandleAsync_clears_the_preference_without_reading_the_catalog()
    {
        await using var dbContext = _fixture.CreateContext();
        var run = await SeedVettedCodexTargetAndRunAsync(dbContext);
        run.SetRequestedCodexAssignment("gpt-6-sol", "high");
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var adapter = new FakeCodexModelCatalogAdapter();
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.RequestedModel);
        Assert.Null(result.Value.RequestedEffort);
        Assert.Equal(0, adapter.CallCount);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await using var freshDbContext = _fixture.CreateContext();
        var reloaded = await freshDbContext.Runs.SingleAsync(candidate => candidate.Id == run.Id);
        Assert.Null(reloaded.RequestedCodexModel);
        Assert.Null(reloaded.RequestedCodexEffort);
        var recordedEvent = await freshDbContext.Events.SingleAsync(candidate => candidate.RunId == run.Id);
        Assert.Equal(RunEventType.CodexAssignmentPreferenceChanged, recordedEvent.EventType);
    }

    [Fact]
    public async Task HandleAsync_sets_a_valid_visible_model_and_supported_effort()
    {
        await using var dbContext = _fixture.CreateContext();
        var run = await SeedVettedCodexTargetAndRunAsync(dbContext);
        var adapter = new FakeCodexModelCatalogAdapter { Observation = ObservedCatalog() };
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, "gpt-6-sol", "high"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("gpt-6-sol", result.Value.RequestedModel);
        Assert.Equal("high", result.Value.RequestedEffort);
        Assert.Equal(1, adapter.CallCount);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        await using var freshDbContext = _fixture.CreateContext();
        var reloaded = await freshDbContext.Runs.SingleAsync(candidate => candidate.Id == run.Id);
        Assert.Equal("gpt-6-sol", reloaded.RequestedCodexModel);
        Assert.Equal("high", reloaded.RequestedCodexEffort);
    }

    [Fact]
    public async Task HandleAsync_sets_a_model_with_no_effort_preference()
    {
        await using var dbContext = _fixture.CreateContext();
        var run = await SeedVettedCodexTargetAndRunAsync(dbContext);
        var adapter = new FakeCodexModelCatalogAdapter { Observation = ObservedCatalog() };
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, "gpt-6-mini", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("gpt-6-mini", result.Value.RequestedModel);
        Assert.Null(result.Value.RequestedEffort);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_model_not_visible_in_the_catalog()
    {
        await using var dbContext = _fixture.CreateContext();
        var run = await SeedVettedCodexTargetAndRunAsync(dbContext);
        var adapter = new FakeCodexModelCatalogAdapter { Observation = ObservedCatalog() };
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, "gpt-unknown-model", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CodexAssignmentPreferenceErrors.ModelNotVisibleCode, result.Errors[0].Code);

        await using var freshDbContext = _fixture.CreateContext();
        var reloaded = await freshDbContext.Runs.SingleAsync(candidate => candidate.Id == run.Id);
        Assert.Null(reloaded.RequestedCodexModel);
    }

    [Fact]
    public async Task HandleAsync_rejects_an_effort_not_supported_by_the_chosen_model()
    {
        await using var dbContext = _fixture.CreateContext();
        var run = await SeedVettedCodexTargetAndRunAsync(dbContext);
        var adapter = new FakeCodexModelCatalogAdapter { Observation = ObservedCatalog() };
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, "gpt-6-mini", "high"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CodexAssignmentPreferenceErrors.EffortNotSupportedCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_new_selection_when_the_catalog_observation_is_unknown()
    {
        await using var dbContext = _fixture.CreateContext();
        var run = await SeedVettedCodexTargetAndRunAsync(dbContext);
        var adapter = new FakeCodexModelCatalogAdapter { Observation = CodexModelCatalogObservation.Unknown };
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, "gpt-6-sol", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CodexAssignmentPreferenceErrors.CatalogUnavailableCode, result.Errors[0].Code);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_new_selection_when_no_codex_launch_target_is_vetted()
    {
        await using var dbContext = _fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "Project", $@"C:\repos\{Guid.NewGuid():N}", Now);
        dbContext.Projects.Add(project);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Objective", Now);
        dbContext.Runs.Add(run);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var adapter = new FakeCodexModelCatalogAdapter { Observation = ObservedCatalog() };
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, "gpt-6-sol", null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CodexAssignmentPreferenceErrors.CatalogUnavailableCode, result.Errors[0].Code);
        Assert.Equal(0, adapter.CallCount);
    }

    [Fact]
    public async Task HandleAsync_rejects_a_change_once_the_run_reaches_a_terminal_lifecycle()
    {
        await using var dbContext = _fixture.CreateContext();
        var run = await SeedVettedCodexTargetAndRunAsync(dbContext);
        run.Claim(Now);
        run.Complete(Now.AddMinutes(1));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var adapter = new FakeCodexModelCatalogAdapter();
        var handler = new SetCodexAssignmentPreferenceCommandHandler(dbContext, adapter, TimeProviderStub());

        var result = await handler.HandleAsync(
            new SetCodexAssignmentPreferenceCommand(run.Id, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CodexAssignmentPreferenceErrors.RunNotEditableCode, result.Errors[0].Code);
    }

    private static TimeProvider TimeProviderStub() => new FixedTimeProvider(Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
