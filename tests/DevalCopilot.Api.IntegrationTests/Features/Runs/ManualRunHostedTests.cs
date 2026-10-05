using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevalCopilot.Api.Features.Projects.GetProjectRunSummaries;
using DevalCopilot.Api.Features.Runs.CreateManualRun;
using DevalCopilot.Api.Features.Runs.GetRunCockpit;
using DevalCopilot.Api.Features.Runs.RequestCodexPlanningAttempt;
using DevalCopilot.Api.Features.Runs.StartSimulatedRun;
using DevalCopilot.Api.HostedServices;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using DevalCopilot.Application.Features.EnvironmentReadiness.Ports;
using DevalCopilot.Application.Features.Processes.Ports;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using DevalCopilot.Infrastructure.Features.Processes;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs;

/// <summary>
/// The hosted proof of manual Agent run intake: the real Api host with the deterministic simulator AND the real
/// Agent supervisor actually registered, reached only through the protected HTTP operations. Only the two external
/// boundaries are doubled: the Codex provider adapter (a counting deterministic double, never a real provider) and the
/// Git evidence reader. Every run, attempt, and proposal being proved is produced by the host itself; only the
/// owned-workspace fixture rows (project, workspace, lease, checkpoint, observed capability) are seeded.
/// </summary>
public sealed class ManualRunHostedTests : IDisposable
{
    private static readonly string Fingerprint = new('a', 64);

    private static readonly string ValidProposalJson = JsonSerializer.Serialize(new
    {
        summary = "Add the ledger table and its query.",
        scope = "Ledger",
        implementationSteps = "Add the table then the query",
        risks = "Unbounded content",
        verificationPlan = "Tests",
        escalationPoints = "None expected",
    });

    private readonly HostStorage _storage = new();
    private readonly CountingCodexAdapter _adapter;

    public ManualRunHostedTests()
    {
        _adapter = new CountingCodexAdapter(_storage.ArtifactStore, ValidProposalJson);
    }

    public void Dispose() => _storage.Dispose();

    private ManualRunHost StartHost() => new(_storage, _adapter);

    private static HttpClient Client(ManualRunHost host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ManualRunHost.Secret);
        return client;
    }

    private static async Task<Guid> SeedProjectAsync(ManualRunHost host, string name, bool withEligibleWorkspace)
    {
        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var project = Project.Register(Guid.NewGuid(), name, $@"C:\repos\{Guid.NewGuid():N}", now);
        dbContext.Projects.Add(project);

        if (withEligibleWorkspace)
        {
            var workspace = GitWorkspace.Prepare(
                Guid.NewGuid(), project.Id, 1, $@"C:\workspaces\{Guid.NewGuid():N}", "branch", new string('a', 40), "main", now);
            workspace.MarkReady();
            dbContext.GitWorkspaces.Add(workspace);
            dbContext.GitCheckpoints.Add(GitCheckpoint.Capture(Guid.NewGuid(), workspace.Id, 1, now, new string('a', 40), Fingerprint, []));
            dbContext.RepositoryMutationLeases.Add(
                RepositoryMutationLease.Acquire(Guid.NewGuid(), project.Id, workspace.Id, 1, Guid.NewGuid().ToByteArray(), now));
            var codex = await dbContext.HostCapabilitySnapshots.SingleAsync(snapshot => snapshot.Capability == Capability.CodexCli);
            codex.MarkDispatched(now);
            codex.RecordSuccess(CapabilityLaunchKind.DirectExecutable, @"C:\safe\codex.exe", null, "1.2.3", now, now.AddMinutes(5));
        }

        await dbContext.SaveChangesAsync();
        return project.Id;
    }

    private static async Task<CreateManualRunResponse> CreateManualRunAsync(
        HttpClient client, Guid projectId, string objective, int? maximumAgentAttempts = null, int? maximumAgentInvocationMinutes = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/runs/manual", new CreateManualRunRequest(projectId, objective, maximumAgentAttempts, maximumAgentInvocationMinutes));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateManualRunResponse>())!;
    }

    private static async Task<GetRunCockpitResponse> CockpitAsync(HttpClient client, Guid runId) =>
        (await client.GetFromJsonAsync<GetRunCockpitResponse>($"/api/runs/{runId}/cockpit"))!;

    private static async Task AssertUntouchedManualRunAsync(ManualRunHost host, HttpClient client, Guid runId, string objective)
    {
        var cockpit = await CockpitAsync(client, runId);
        Assert.Equal("Created", cockpit.Lifecycle);
        Assert.Equal("Intake", cockpit.Stage);
        Assert.Equal("ManualAgent", cockpit.ExecutionMode);
        Assert.Equal(objective, cockpit.Objective);
        Assert.Equal(0, cockpit.AgentAttemptsUsed);

        using var scope = host.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        Assert.Empty(await dbContext.Attempts.Where(attempt => attempt.RunId == runId).ToListAsync());
        Assert.Empty(await dbContext.CollaborationMessages.Where(message => message.RunId == runId).ToListAsync());
        Assert.Empty(await dbContext.Artifacts.Where(artifact => artifact.RunId == runId).ToListAsync());
        var events = await dbContext.Events.Where(runEvent => runEvent.RunId == runId).ToListAsync();
        Assert.Equal([RunEventType.RunStarted], events.Select(runEvent => runEvent.EventType));
    }

    /// <summary>Several simulator polls (200 ms) and Agent supervisor polls (500 ms).</summary>
    private static Task SeveralPollCyclesAsync() => Task.Delay(TimeSpan.FromMilliseconds(2000));

    [Fact]
    public async Task A_manual_run_created_over_http_stays_created_across_poll_cycles_and_a_host_restart_while_a_simulation_progresses()
    {
        Guid manualProjectId;
        Guid simulatedProjectId;
        Guid manualRunId;
        const string objective = "Record the ledger migration plan";

        using (var host = StartHost())
        {
            using var client = Client(host);
            manualProjectId = await SeedProjectAsync(host, "Manual fixture", withEligibleWorkspace: true);
            simulatedProjectId = await SeedProjectAsync(host, "Simulation fixture", withEligibleWorkspace: false);

            var manual = await CreateManualRunAsync(client, manualProjectId, objective);
            manualRunId = manual.RunId;
            Assert.Equal(1, manual.ExecutionNumber);

            // Positive control: an explicit simulation for another project does progress to its terminal state.
            var simulatedResponse = await client.PostAsJsonAsync(
                "/api/runs/simulated", new StartSimulatedRunRequest(simulatedProjectId, "Prove the walking skeleton"));
            simulatedResponse.EnsureSuccessStatusCode();
            var simulated = await simulatedResponse.Content.ReadFromJsonAsync<StartSimulatedRunResponse>();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            GetRunCockpitResponse simulatedCockpit;
            do
            {
                simulatedCockpit = await CockpitAsync(client, simulated!.RunId);
                if (simulatedCockpit.Lifecycle == "Completed")
                {
                    break;
                }

                await Task.Delay(100);
            }
            while (DateTime.UtcNow < deadline);

            Assert.Equal("Completed", simulatedCockpit.Lifecycle);
            Assert.Equal("Simulated", simulatedCockpit.ExecutionMode);

            await SeveralPollCyclesAsync();
            await AssertUntouchedManualRunAsync(host, client, manualRunId, objective);

            // The same protected operation refuses a second intent while the manual run is unfinished.
            var blocked = await client.PostAsJsonAsync("/api/runs/manual", new CreateManualRunRequest(manualProjectId, "Another"));
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Contains("runs.intent_blocked", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            var summaries = await client.GetFromJsonAsync<List<ProjectRunSummaryResponse>>("/api/projects/run-summaries");
            var summary = Assert.Single(summaries!, candidate => candidate.ProjectId == manualProjectId);
            Assert.Equal("ManualAgent", summary.ExecutionMode);
            Assert.False(summary.CanCreateRun);
            Assert.Equal(manualRunId, summary.RunId);
        }

        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_storage.DatabasePath}"));

        // A host restart over the same database: the manual run is found exactly as it was left.
        using (var restarted = StartHost())
        {
            using var client = Client(restarted);
            await SeveralPollCyclesAsync();
            await AssertUntouchedManualRunAsync(restarted, client, manualRunId, objective);
        }

        Assert.Equal(0, _adapter.InvocationCount);
    }

    [Fact]
    public async Task An_explicit_planning_request_produces_one_validated_proposal_with_correct_budgets_and_no_simulated_attempt()
    {
        using var host = StartHost();
        using var client = Client(host);
        var projectId = await SeedProjectAsync(host, "Planning fixture", withEligibleWorkspace: true);
        var manual = await CreateManualRunAsync(client, projectId, "Plan the ledger increment");

        await SeveralPollCyclesAsync();
        Assert.Equal(0, _adapter.InvocationCount);

        var requested = await client.PostAsync($"/api/runs/{manual.RunId}/agent-attempts/codex-plan", content: null);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var attempt = await requested.Content.ReadFromJsonAsync<RequestCodexPlanningAttemptResponse>();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        AttemptStatus status;
        do
        {
            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            status = (await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attempt!.AttemptId)).Status;
            if (status != AttemptStatus.Running)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Equal(AttemptStatus.Completed, status);
        await SeveralPollCyclesAsync();

        using var verifyScope = host.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
        var attempts = await verify.Attempts.AsNoTracking().Where(candidate => candidate.RunId == manual.RunId).ToListAsync();
        var planning = Assert.Single(attempts);
        Assert.Equal(AttemptKind.Agent, planning.Kind);
        Assert.Equal(AgentOutcome.Proposed, planning.AgentOutcome);
        Assert.DoesNotContain(attempts, candidate => candidate.Kind == AttemptKind.Simulated);

        var proposal = Assert.Single(await verify.CollaborationMessages.AsNoTracking().Where(message => message.RunId == manual.RunId).ToListAsync());
        Assert.Equal(CollaborationMessageType.Proposal, proposal.Type);
        Assert.Equal(CollaborationMessageProvenance.ProviderObserved, proposal.Provenance);
        Assert.Equal(planning.Id, proposal.AttemptId);

        var cockpit = await CockpitAsync(client, manual.RunId);
        Assert.Equal("ManualAgent", cockpit.ExecutionMode);
        Assert.Equal(16, cockpit.MaximumAgentAttempts);
        Assert.Equal(1, cockpit.AgentAttemptsUsed);
        Assert.False(cockpit.AgentBudgetExhausted);
        Assert.Equal((long)TimeSpan.FromMinutes(120).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
        Assert.Equal((long)planning.AgentTimeout!.Value.TotalMilliseconds, cockpit.AgentInvocationTimeBudget.ReservedMilliseconds);
        Assert.Equal(1, _adapter.InvocationCount);
    }

    private static async Task<AttemptStatus> WaitForAttemptAsync(ManualRunHost host, Guid attemptId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        AttemptStatus status;
        do
        {
            using var scope = host.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DevalCopilotDbContext>();
            status = (await dbContext.Attempts.AsNoTracking().SingleAsync(candidate => candidate.Id == attemptId)).Status;
            if (status != AttemptStatus.Running)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return status;
    }

    [Fact]
    public async Task A_chosen_reservation_below_the_planning_timeout_refuses_the_claim_before_any_provider_work()
    {
        using var host = StartHost();
        using var client = Client(host);
        var projectId = await SeedProjectAsync(host, "Small time fixture", withEligibleWorkspace: true);
        var manual = await CreateManualRunAsync(client, projectId, "Plan within nine minutes", maximumAgentAttempts: 5, maximumAgentInvocationMinutes: 9);

        var requested = await client.PostAsync($"/api/runs/{manual.RunId}/agent-attempts/codex-plan", content: null);

        Assert.Equal(HttpStatusCode.Conflict, requested.StatusCode);
        Assert.Contains("agent_attempts.time_budget_exceeded", await requested.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await SeveralPollCyclesAsync();
        await AssertUntouchedManualRunAsync(host, client, manual.RunId, "Plan within nine minutes");
        var cockpit = await CockpitAsync(client, manual.RunId);
        Assert.Equal(5, cockpit.MaximumAgentAttempts);
        Assert.Equal((long)TimeSpan.FromMinutes(9).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
        Assert.Equal(0L, cockpit.AgentInvocationTimeBudget.ReservedMilliseconds);
        Assert.Equal(0, _adapter.InvocationCount);
    }

    [Fact]
    public async Task A_chosen_single_claim_with_an_exactly_fitting_reservation_is_consumed_permanently_and_then_refused()
    {
        using var host = StartHost();
        using var client = Client(host);
        var projectId = await SeedProjectAsync(host, "Equality fixture", withEligibleWorkspace: true);
        var manual = await CreateManualRunAsync(client, projectId, "Plan within ten minutes", maximumAgentAttempts: 1, maximumAgentInvocationMinutes: 10);

        var requested = await client.PostAsync($"/api/runs/{manual.RunId}/agent-attempts/codex-plan", content: null);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var attempt = await requested.Content.ReadFromJsonAsync<RequestCodexPlanningAttemptResponse>();
        Assert.Equal(AttemptStatus.Completed, await WaitForAttemptAsync(host, attempt!.AttemptId));
        await SeveralPollCyclesAsync();

        var cockpit = await CockpitAsync(client, manual.RunId);
        Assert.Equal(1, cockpit.MaximumAgentAttempts);
        Assert.Equal(1, cockpit.AgentAttemptsUsed);
        Assert.True(cockpit.AgentBudgetExhausted);
        Assert.Equal((long)TimeSpan.FromMinutes(10).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
        Assert.Equal((long)TimeSpan.FromMinutes(10).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.ReservedMilliseconds);
        Assert.Equal(0L, cockpit.AgentInvocationTimeBudget.RemainingMilliseconds);

        var second = await client.PostAsync($"/api/runs/{manual.RunId}/agent-attempts/codex-plan", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("agent_attempts.budget_exhausted", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, _adapter.InvocationCount);
    }

    [Fact]
    public async Task A_smaller_chosen_budget_on_one_run_never_limits_an_unrelated_default_run()
    {
        using var host = StartHost();
        using var client = Client(host);
        var smallProject = await SeedProjectAsync(host, "Small fixture", withEligibleWorkspace: true);
        var defaultProject = await SeedProjectAsync(host, "Default fixture", withEligibleWorkspace: true);
        var small = await CreateManualRunAsync(client, smallProject, "Too small", maximumAgentAttempts: 1, maximumAgentInvocationMinutes: 1);
        var unrelated = await CreateManualRunAsync(client, defaultProject, "Default budgets");

        var refused = await client.PostAsync($"/api/runs/{small.RunId}/agent-attempts/codex-plan", content: null);
        var accepted = await client.PostAsync($"/api/runs/{unrelated.RunId}/agent-attempts/codex-plan", content: null);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var cockpit = await CockpitAsync(client, unrelated.RunId);
        Assert.Equal(16, cockpit.MaximumAgentAttempts);
        Assert.Equal((long)TimeSpan.FromMinutes(120).TotalMilliseconds, cockpit.AgentInvocationTimeBudget.MaximumMilliseconds);
    }

    /// <summary>Disposable owned fixture roots, shared by a host and its restarted successor.</summary>
    private sealed class HostStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"devalcopilot-manual-run-hosted-{Guid.NewGuid():N}");

        public HostStorage()
        {
            Directory.CreateDirectory(_root);
            ArtifactStore = new FilesystemArtifactStore(ArtifactRoot);
        }

        public string DatabasePath => Path.Combine(_root, "host.db");

        public string ArtifactRoot => Path.Combine(_root, "artifacts");

        public string WorkspaceRoot => Path.Combine(_root, "workspaces");

        public FilesystemArtifactStore ArtifactStore { get; }

        public void Dispose()
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={DatabasePath}"));
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class CountingCodexAdapter(IArtifactStore artifactStore, string finalResponseJson) : ICodexPlanningAdapter
    {
        private int _invocationCount;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public async Task<CodexPlanningInvocationResult> InvokeAsync(
            CodexPlanningInvocationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            var path = artifactStore.GetPartialPath(request.RunId, request.AttemptId, ArtifactPurpose.AgentFinalResponse);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, finalResponseJson, cancellationToken);
            return new CodexPlanningInvocationResult(
                CodexPlanningInvocationOutcome.Exited, false, false, null, ProcessEvidence: TestProcessEvidence.ReportedCleanExit);
        }
    }

    private sealed class FixedEvidenceReader : IGitWorkspaceEvidenceReader
    {
        public Task<GitWorkspaceEvidenceResult> CaptureAsync(string workspacePath, CancellationToken cancellationToken) =>
            Task.FromResult(new GitWorkspaceEvidenceResult(
                GitWorkspaceEvidenceOutcome.Success, new string('a', 40), Fingerprint, [], null));
    }

    private sealed class ThrowingToolDiscovery : IToolDiscoveryAdapter
    {
        public Task<ToolDiscoveryResult> DiscoverAsync(Capability capability, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Hosted manual-run tests must never probe a provider executable.");
    }

    /// <summary>The real Api host: authenticated HTTP, the real simulator and Agent supervisors, and only the
    /// provider adapter and Git evidence reader replaced.</summary>
    private sealed class ManualRunHost(HostStorage storage, ICodexPlanningAdapter adapter) : WebApplicationFactory<Program>
    {
        public const string Secret = ApiWebApplicationFactory.ValidSecret;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("LaunchSession:Secret", Secret),
                new KeyValuePair<string, string?>("ConnectionStrings:DevalCopilot", $"Data Source={storage.DatabasePath}"),
                new KeyValuePair<string, string?>("Logging:EventLog:LogLevel:Default", "None"),
            ]));

            builder.ConfigureTestServices(services =>
            {
                foreach (var hostedService in services
                             .Where(descriptor => descriptor.ServiceType == typeof(IHostedService)
                                 && descriptor.ImplementationType?.Namespace?.StartsWith(
                                     "DevalCopilot.Api.HostedServices", StringComparison.Ordinal) == true)
                             .ToArray())
                {
                    services.Remove(hostedService);
                }

                services.RemoveAll<IToolDiscoveryAdapter>();
                services.AddSingleton<IToolDiscoveryAdapter, ThrowingToolDiscovery>();
                services.RemoveAll<IArtifactStore>();
                services.RemoveAll<IVerificationOutputArtifactStore>();
                services.RemoveAll<IWorkspaceRootPathProvider>();
                services.AddSingleton<IArtifactStore>(storage.ArtifactStore);
                services.AddSingleton<IVerificationOutputArtifactStore>(storage.ArtifactStore);
                services.AddSingleton<IWorkspaceRootPathProvider>(new WorkspaceRootPathProvider(storage.WorkspaceRoot));
                services.RemoveAll<IGitWorkspaceEvidenceReader>();
                services.AddSingleton<IGitWorkspaceEvidenceReader, FixedEvidenceReader>();
                services.RemoveAll<ICodexPlanningAdapter>();
                services.AddSingleton(adapter);

                // The simulator and the real Agent supervisor are genuinely registered.
                services.AddHostedService<SimulatedRunSupervisor>();
                services.AddHostedService<AgentAttemptSupervisor>();
            });
        }
    }
}
