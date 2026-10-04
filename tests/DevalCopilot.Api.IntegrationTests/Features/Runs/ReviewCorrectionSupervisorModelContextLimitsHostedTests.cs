using System.Text.Json;
using Devalente.Shared.Cqrs;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.ReconcileInterruptedImplementationAttempts;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The real <c>ReviewCorrectionSupervisor</c> with the REAL <see cref="ClaudeReviewCorrectionAdapter"/> over a deterministic
/// provider process, the real mediator, EF transaction pipeline, production recording command and a file-backed SQLite
/// database: the models a provider lists, with their reported limits, are recorded in the completion transaction and read back
/// through the production evidence query. Only the process and the Git evidence reader are test boundaries.
/// </summary>
public sealed partial class ReviewCorrectionSupervisorHostedTests
{
    private sealed record RealCorrection(
        AttemptStatus Status,
        AgentOutcome? Outcome,
        string? Snapshot,
        GetAgentAttemptEvidenceQueryResult Evidence,
        IReadOnlyList<ProcessExecutionRequest> Requests);

    private static string ValidCorrectionResponse(Guid findingId) => JsonSerializer.Serialize(new
    {
        revisionResponses = new[]
        {
            new
            {
                findingMessageId = findingId,
                disposition = "Fixed",
                evidence = "The incomplete branch was corrected.",
                resultingSourceChanges = "Completed the branch.",
            },
        },
        executionReport = new
        {
            summary = "Correction complete.",
            changedRelativePaths = new[] { "src/Foo.cs" },
            implementationNotes = "Applied the requested correction.",
            unexpectedDiscoveries = "None.",
            remainingRisks = "None.",
            recommendedVerification = "Run tests.",
        },
    });

    private async Task<RealCorrection> RunRealAdapterCorrectionAsync(Func<Guid, string> standardOutputForFinding)
    {
        var evidence = new SequencedEvidence(call => call == 1 ? Matching() : Changed());
        var notifier = new TestNotifier();
        var launcher = ClaudeEnvelopeProcessDouble.CreateLaunchFile(Path.Combine(_artifactRoot, "launcher"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddDbContextFactory<DevalCopilotDbContext>(options => options.UseSqlite($"Data Source={_databasePath}"));
        services.AddScoped<IDevalCopilotDbContext>(sp => sp.GetRequiredService<DevalCopilotDbContext>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGitWorkspaceEvidenceReader>(evidence);
        services.AddSingleton<IArtifactStore>(_artifactStore);
        services.AddSingleton<IRunEventNotifier>(notifier);
        services.AddDevalenteMediator(typeof(ReconcileInterruptedImplementationAttemptsCommand).Assembly);
        services.AddDevalenteRequestValidation(typeof(ReconcileInterruptedImplementationAttemptsCommand).Assembly);
        services.AddDevalenteEfCoreTransactions<DevalCopilotDbContext>();
        await using var provider = services.BuildServiceProvider();

        var seed = await SeedAsync(provider, claudeLaunchPath: launcher, manifestText: "{}");
        var process = new ClaudeEnvelopeProcessDouble(standardOutputForFinding(seed.FindingId));
        var supervisor = new ReviewCorrectionSupervisor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ClaudeReviewCorrectionAdapter(process, _artifactStore),
            evidence,
            _artifactStore,
            NullLogger<ReviewCorrectionSupervisor>.Instance);
        await supervisor.StartAsync(CancellationToken.None);
        try
        {
            await notifier.Notified.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await supervisor.StopAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
        }

        await using var scope = provider.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>()
            .Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == seed.CorrectionId);
        var read = await ClaudeEnvelopeProcessDouble.ReadEvidenceAsync(
            scope.ServiceProvider.GetRequiredService<IApplicationMediator>(), seed.RunId, seed.CorrectionId);
        return new RealCorrection(persisted.Status, persisted.AgentOutcome, persisted.AgentModelContextLimitsSnapshot, read, process.Requests);
    }

    [Fact]
    public async Task The_real_adapter_records_every_listed_model_with_its_limits_beside_the_usage_and_an_applied_correction()
    {
        var recorded = await RunRealAdapterCorrectionAsync(finding => ClaudeEnvelopeProcessDouble.Envelope(
            ValidCorrectionResponse(finding), ClaudeEnvelopeProcessDouble.ValidUsage, ClaudeEnvelopeProcessDouble.TwoModelUsage));

        Assert.Equal(AttemptStatus.Completed, recorded.Status);
        Assert.Equal(AgentOutcome.CorrectionApplied, recorded.Outcome);
        Assert.Equal(ClaudeEnvelopeProcessDouble.TwoModelSnapshot, recorded.Snapshot);
        ClaudeEnvelopeProcessDouble.AssertTwoModels(recorded.Evidence.ModelContextLimits);
        Assert.Equal(1200, recorded.Evidence.TokenUsage!.InputTokens);
        Assert.Single(recorded.Requests);
    }

    [Fact]
    public async Task The_limits_are_recorded_even_when_the_business_output_is_not_a_valid_correction()
    {
        var recorded = await RunRealAdapterCorrectionAsync(_ => ClaudeEnvelopeProcessDouble.Envelope(
            "this is not the correction contract", ClaudeEnvelopeProcessDouble.TwoModelUsage));

        Assert.Equal(AttemptStatus.Failed, recorded.Status);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, recorded.Outcome);
        Assert.Equal(ClaudeEnvelopeProcessDouble.TwoModelSnapshot, recorded.Snapshot);
        ClaudeEnvelopeProcessDouble.AssertTwoModels(recorded.Evidence.ModelContextLimits);
    }

    [Fact]
    public async Task The_limits_are_recorded_for_a_valid_error_envelope_that_the_provider_reported()
    {
        var recorded = await RunRealAdapterCorrectionAsync(_ => ClaudeEnvelopeProcessDouble.Envelope(
            isError: true, "the provider stopped", ClaudeEnvelopeProcessDouble.ValidUsage, ClaudeEnvelopeProcessDouble.TwoModelUsage));

        Assert.Equal(AttemptStatus.Failed, recorded.Status);
        Assert.Equal(AgentOutcome.ProviderInvocationFailed, recorded.Outcome);
        Assert.Equal(ClaudeEnvelopeProcessDouble.TwoModelSnapshot, recorded.Snapshot);
        ClaudeEnvelopeProcessDouble.AssertTwoModels(recorded.Evidence.ModelContextLimits);
        Assert.Equal(345, recorded.Evidence.TokenUsage!.OutputTokens);
    }

    [Theory]
    [InlineData(ClaudeEnvelopeProcessDouble.MalformedModelUsage)]
    [InlineData("\"modelUsage\":{}")]
    [InlineData("\"unrelated\":1")]
    public async Task An_unusable_map_leaves_the_applied_correction_and_the_usage_intact_and_records_no_limits(string modelUsage)
    {
        var recorded = await RunRealAdapterCorrectionAsync(finding => ClaudeEnvelopeProcessDouble.Envelope(
            ValidCorrectionResponse(finding), ClaudeEnvelopeProcessDouble.ValidUsage, modelUsage));

        Assert.Equal(AttemptStatus.Completed, recorded.Status);
        Assert.Equal(AgentOutcome.CorrectionApplied, recorded.Outcome);
        Assert.Null(recorded.Snapshot);
        Assert.Null(recorded.Evidence.ModelContextLimits);
        Assert.Equal(1200, recorded.Evidence.TokenUsage!.InputTokens);
    }

    [Fact]
    public async Task Missing_usage_never_discards_the_limits()
    {
        var recorded = await RunRealAdapterCorrectionAsync(finding => ClaudeEnvelopeProcessDouble.Envelope(
            ValidCorrectionResponse(finding), ClaudeEnvelopeProcessDouble.TwoModelUsage));

        Assert.Equal(AgentOutcome.CorrectionApplied, recorded.Outcome);
        Assert.Null(recorded.Evidence.TokenUsage);
        Assert.Equal(ClaudeEnvelopeProcessDouble.TwoModelSnapshot, recorded.Snapshot);
    }
}
