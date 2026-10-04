using Devalente.Shared.Cqrs;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetAgentAttemptEvidence;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Runs;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The real <c>ImplementationSupervisor</c> with the REAL <see cref="ClaudeImplementationAdapter"/> over a deterministic
/// provider process, the real mediator, EF transaction pipeline, production recording command and a file-backed SQLite
/// database: the models a provider lists, with their reported limits, are recorded in the completion transaction and read back
/// through the production evidence query. Only the process and the Git evidence reader are test boundaries.
/// </summary>
public sealed partial class ImplementationSupervisorHostedTests
{
    private sealed record RealImplementation(
        AttemptStatus Status,
        AgentOutcome? Outcome,
        string? Snapshot,
        GetAgentAttemptEvidenceQueryResult Evidence,
        IReadOnlyList<ProcessExecutionRequest> Requests);

    private async Task<RealImplementation> RunRealAdapterImplementationAsync(string standardOutput)
    {
        var evidenceReader = new SequencedGitWorkspaceEvidenceReader(call =>
            call <= 4
                ? Matching(Fingerprint)
                : MatchingWithChangedPaths(ChangedFingerprint, [new GitWorkspaceChangedPath("src/Foo.cs", null, "M", " ")]));
        var process = new ClaudeEnvelopeProcessDouble(standardOutput);
        var adapter = new ClaudeImplementationAdapter(process, _artifactStore);

        await using var provider = BuildServiceProvider(evidenceReader, adapter);
        await PreseedRealClaudeLauncherAsync(provider);
        var (runId, attemptId, _, _) = await SeedEligibleImplementationAttemptAsync(provider, evidenceReader);

        await RunSupervisorToTerminalAsync(provider, attemptId);

        await using var scope = provider.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>()
            .Attempts.AsNoTracking().SingleAsync(attempt => attempt.Id == attemptId);
        var evidence = await ClaudeEnvelopeProcessDouble.ReadEvidenceAsync(
            scope.ServiceProvider.GetRequiredService<IApplicationMediator>(), runId, attemptId);
        return new RealImplementation(persisted.Status, persisted.AgentOutcome, persisted.AgentModelContextLimitsSnapshot, evidence, process.Requests);
    }

    private async Task PreseedRealClaudeLauncherAsync(ServiceProvider provider)
    {
        var launcher = ClaudeEnvelopeProcessDouble.CreateLaunchFile(Path.Combine(_artifactRoot, "launcher"));
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        await dbContext.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var claude = HostCapabilitySnapshot.Seed(Capability.ClaudeCli, now);
        claude.MarkDispatched(now);
        claude.RecordSuccess(CapabilityLaunchKind.DirectExecutable, launcher, null, "2.1.276", now, now.AddMinutes(5));
        dbContext.HostCapabilitySnapshots.Add(claude);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_real_adapter_records_every_listed_model_with_its_limits_beside_the_usage_and_an_implemented_outcome()
    {
        var recorded = await RunRealAdapterImplementationAsync(ClaudeEnvelopeProcessDouble.Envelope(
            ValidImplementationReportJson(["src/Foo.cs"]), ClaudeEnvelopeProcessDouble.ValidUsage, ClaudeEnvelopeProcessDouble.TwoModelUsage));

        Assert.Equal(AttemptStatus.Completed, recorded.Status);
        Assert.Equal(AgentOutcome.Implemented, recorded.Outcome);
        Assert.Equal(ClaudeEnvelopeProcessDouble.TwoModelSnapshot, recorded.Snapshot);
        ClaudeEnvelopeProcessDouble.AssertTwoModels(recorded.Evidence.ModelContextLimits);
        Assert.Equal(1200, recorded.Evidence.TokenUsage!.InputTokens);
        Assert.Single(recorded.Requests);
    }

    [Fact]
    public async Task The_limits_are_recorded_even_when_the_business_output_is_not_a_valid_report()
    {
        var recorded = await RunRealAdapterImplementationAsync(ClaudeEnvelopeProcessDouble.Envelope(
            "this is not the report contract", ClaudeEnvelopeProcessDouble.TwoModelUsage));

        Assert.Equal(AttemptStatus.Failed, recorded.Status);
        Assert.Equal(AgentOutcome.InvalidStructuredOutput, recorded.Outcome);
        Assert.Equal(ClaudeEnvelopeProcessDouble.TwoModelSnapshot, recorded.Snapshot);
        ClaudeEnvelopeProcessDouble.AssertTwoModels(recorded.Evidence.ModelContextLimits);
    }

    [Fact]
    public async Task The_limits_are_recorded_for_a_valid_error_envelope_that_the_provider_reported()
    {
        var recorded = await RunRealAdapterImplementationAsync(ClaudeEnvelopeProcessDouble.Envelope(
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
    public async Task An_unusable_map_leaves_the_implemented_outcome_and_the_usage_intact_and_records_no_limits(string modelUsage)
    {
        var recorded = await RunRealAdapterImplementationAsync(ClaudeEnvelopeProcessDouble.Envelope(
            ValidImplementationReportJson(["src/Foo.cs"]), ClaudeEnvelopeProcessDouble.ValidUsage, modelUsage));

        Assert.Equal(AttemptStatus.Completed, recorded.Status);
        Assert.Equal(AgentOutcome.Implemented, recorded.Outcome);
        Assert.Null(recorded.Snapshot);
        Assert.Null(recorded.Evidence.ModelContextLimits);
        Assert.Equal(1200, recorded.Evidence.TokenUsage!.InputTokens);
    }

    [Fact]
    public async Task Missing_usage_never_discards_the_limits()
    {
        var recorded = await RunRealAdapterImplementationAsync(ClaudeEnvelopeProcessDouble.Envelope(
            ValidImplementationReportJson(["src/Foo.cs"]), ClaudeEnvelopeProcessDouble.TwoModelUsage));

        Assert.Equal(AgentOutcome.Implemented, recorded.Outcome);
        Assert.Null(recorded.Evidence.TokenUsage);
        Assert.Equal(ClaudeEnvelopeProcessDouble.TwoModelSnapshot, recorded.Snapshot);
    }
}
