using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0056: an Agent supervision observation is a non-authoritative disclosure and never execution authority. Only its own
/// commands and sweep write the observation, none of them reaches an Attempt, Run, workspace, budget, dispatch or provider transition,
/// no claim, dispatch, eligibility or recovery source reads it, and the observation collaborator is wired only into the seven
/// existing Agent supervisors.</summary>
public sealed class AgentSupervisionBoundaryTests
{
    private static readonly string[] ObservationSources =
    [
        "DevalCopilot.Application/Features/Runs/Commands/RegisterAgentSupervision/RegisterAgentSupervisionCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/AdvanceAgentSupervisionPhase/AdvanceAgentSupervisionPhaseCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/HeartbeatAgentSupervision/HeartbeatAgentSupervisionCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CloseAgentSupervision/CloseAgentSupervisionCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/ObserveAgentSupervision/ObserveAgentSupervisionCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Commands/CloseInterruptedAgentSupervisions/CloseInterruptedAgentSupervisionsCommandHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetAgentSupervision/GetAgentSupervisionQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Queries/GetOpenAgentSupervisions/GetOpenAgentSupervisionsQueryHandler.cs",
        "DevalCopilot.Application/Features/Runs/Policies/AgentSupervisions/AgentSupervisionClosure.cs",
        "DevalCopilot.Application/Features/Runs/Policies/AgentSupervisions/AgentSupervisionWrites.cs",
        "DevalCopilot.Application/Features/Runs/Policies/AgentSupervisions/AgentSupervisionAttempts.cs",
        "DevalCopilot.Application/Features/Runs/Policies/AgentSupervisions/AgentSupervisionEvents.cs",
        "DevalCopilot.Api/HostedServices/AgentSupervisionObserver.cs",
        "DevalCopilot.Api/HostedServices/AgentObservation.cs",
        "DevalCopilot.Api/HostedServices/AgentSupervisionSweep.cs",
    ];

    private static readonly string[] Supervisors =
    [
        "AgentAttemptSupervisor",
        "ClaudeCriticalReviewSupervisor",
        "ChallengeResolutionSupervisor",
        "ImplementationSupervisor",
        "ImplementationReviewSupervisor",
        "ReviewCorrectionSupervisor",
        "VerificationDiagnosisSupervisor",
    ];

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DevalCopilot.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "backend");
    }

    private static IEnumerable<string> ProductionSources(string root) => Directory
        .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Code(string path) => string.Join(
        '\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string[] SourcesContaining(string root, string text) => ProductionSources(root)
        .Where(path => Code(path).Contains(text, StringComparison.Ordinal))
        .Select(path => Relative(root, path))
        .Order(StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void The_observation_sources_exist_and_nothing_else_writes_an_observation()
    {
        var root = BackendRoot();

        Assert.All(ObservationSources, source => Assert.True(File.Exists(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar))), source));

        // The receipt is created only by its Domain factory through the registration handler and changed only through its own Domain
        // transitions inside the observation handlers and their one closing rule; no other source touches the DbSet.
        Assert.Equal(
            ["DevalCopilot.Application/Features/Runs/Commands/RegisterAgentSupervision/RegisterAgentSupervisionCommandHandler.cs"],
            SourcesContaining(root, "AgentSupervision.Register("));
        var setUsers = SourcesContaining(root, "dbContext.AgentSupervisions");
        Assert.All(
            setUsers.Where(path => !path.StartsWith("DevalCopilot.Infrastructure/", StringComparison.Ordinal)
                && !path.StartsWith("DevalCopilot.Application/Data/", StringComparison.Ordinal)),
            path => Assert.Contains(path, ObservationSources));
    }

    [Fact]
    public void No_observation_source_can_claim_dispatch_terminalize_retry_or_release_anything()
    {
        var root = BackendRoot();
        string[] forbidden =
        [
            ".Complete(", ".CompleteAgent(", ".CompleteImplementation(", ".CompleteReviewCorrection(", ".CompleteProcess(", ".Fail(",
            ".Interrupt(", "MarkAgentDispatched", "MarkProcessDispatched", "Attempt.Claim", "ClaimAgent", "RepositoryMutationLease",
            "GitWorkspace", "Lifecycle =", "Budget", "AgentOutcome =", "Status = AttemptStatus.", "IApplicationMediator", "ICodexPlanningAdapter",
            "IGitWorkspaceEvidenceReader", "IProcessExecutionAdapter", "File.Delete", "Directory.Delete",
        ];

        foreach (var source in ObservationSources)
        {
            var code = Code(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var token in forbidden)
            {
                // The host collaborators dispatch only through the mediator; the Application sources never reach another feature.
                if (token == "IApplicationMediator" && source.StartsWith("DevalCopilot.Api/", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(code.Contains(token, StringComparison.Ordinal), $"{source} must not contain {token}");
            }
        }
    }

    [Fact]
    public void The_only_rows_an_observation_handler_locks_are_its_own_receipt_and_the_run_row_by_a_no_op_that_changes_nothing()
    {
        var root = BackendRoot();

        foreach (var source in ObservationSources.Where(source => source.Contains("/Commands/", StringComparison.Ordinal)
                     || source.EndsWith("AgentSupervisionWrites.cs", StringComparison.Ordinal)))
        {
            var code = Code(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var line in code.Split('\n').Where(line => line.Contains("SetProperty(", StringComparison.Ordinal)))
            {
                Assert.True(
                    line.Contains("candidate.Revision, candidate => candidate.Revision", StringComparison.Ordinal)
                    || line.Contains("candidate.Lifecycle, candidate => candidate.Lifecycle", StringComparison.Ordinal),
                    $"{source}: {line.Trim()}");
            }

            Assert.DoesNotContain("ExecuteDeleteAsync", code, StringComparison.Ordinal);
            Assert.DoesNotContain(".Remove(", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_claim_dispatch_eligibility_source_or_other_supervisor_input_reads_an_observation()
    {
        var root = BackendRoot();
        var readers = ProductionSources(root)
            .Select(path => Relative(root, path))
            .Where(path => path.Contains("Supervision", StringComparison.Ordinal) is false && !path.EndsWith("/AgentObservation.cs", StringComparison.Ordinal))
            .Where(path => Code(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))
                .Contains("AgentSupervision", StringComparison.Ordinal))
            .Where(path => !path.StartsWith("DevalCopilot.Infrastructure/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Only the data context contract, the event-type constants, the host composition root and the seven supervisors name it.
        string[] allowed =
        [
            "DevalCopilot.Api/HostedServices/AgentAttemptSupervisor.cs",
            "DevalCopilot.Api/HostedServices/ChallengeResolutionSupervisor.cs",
            "DevalCopilot.Api/HostedServices/ClaudeCriticalReviewSupervisor.cs",
            "DevalCopilot.Api/HostedServices/ImplementationReviewSupervisor.cs",
            "DevalCopilot.Api/HostedServices/ImplementationSupervisor.cs",
            "DevalCopilot.Api/HostedServices/ReviewCorrectionSupervisor.cs",
            "DevalCopilot.Api/HostedServices/VerificationDiagnosisSupervisor.cs",
            "DevalCopilot.Api/Program.cs",
            "DevalCopilot.Application/Data/IDevalCopilotDbContext.cs",
            "DevalCopilot.Domain/Features/Runs/RunEventType.cs",
        ];
        Assert.Empty(readers.Except(allowed));
    }

    [Fact]
    public void The_seven_supervisors_open_the_observation_at_entry_and_report_only_the_two_later_phases()
    {
        var root = BackendRoot();

        foreach (var supervisor in Supervisors)
        {
            var code = Code(Path.Combine(root, "DevalCopilot.Api", "HostedServices", $"{supervisor}.cs"));

            Assert.Contains("await using var observation = await AgentSupervisionObserver.BeginAsync(scopeFactory,", code, StringComparison.Ordinal);
            Assert.Single(code.Split("AgentSupervisionObserver.BeginAsync(", StringSplitOptions.None).Skip(1));
            Assert.Single(code.Split("observation.EnterAwaitingAdapterResultAsync()", StringSplitOptions.None).Skip(1));
            Assert.True(
                code.Split("observation.EnterRecordingResultAsync()", StringSplitOptions.None).Length - 1 >= 3,
                $"{supervisor} reports RecordingResult before every result recording");

            // The observation is bounded and read-only for the supervisor: it never branches on the observation or its outcome.
            Assert.DoesNotContain("observation.IsActive", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_observation_collaborator_is_in_no_other_supervisor_and_the_sweep_runs_only_in_the_real_composition()
    {
        var root = BackendRoot();

        var users = SourcesContaining(root, "AgentSupervisionObserver.BeginAsync(");
        Assert.Equal(
            Supervisors.Select(supervisor => $"DevalCopilot.Api/HostedServices/{supervisor}.cs").Order(StringComparer.Ordinal),
            users.Where(path => !path.EndsWith("AgentSupervisionObserver.cs", StringComparison.Ordinal)));

        // The sweep is a hosted service of the host's own namespace, so both side-effect-free compositions remove it with the others.
        Assert.StartsWith(
            "namespace DevalCopilot.Api.HostedServices;",
            string.Join('\n', File.ReadAllLines(Path.Combine(root, "DevalCopilot.Api", "HostedServices", "AgentSupervisionSweep.cs")).Where(line => line.StartsWith("namespace", StringComparison.Ordinal))));
    }

    [Fact]
    public void The_status_read_has_one_endpoint_that_dispatches_one_query_and_the_policy_has_one_owner()
    {
        var root = BackendRoot();

        Assert.Equal(
            ["DevalCopilot.Api/Features/Runs/GetAgentSupervision/GetAgentSupervisionEndpoint.cs"],
            SourcesContaining(root, "new GetAgentSupervisionQuery("));
        Assert.Equal(
            ["DevalCopilot.Domain/Features/Runs/AgentSupervisionPolicy.cs"],
            SourcesContaining(root, "TimeSpan.FromSeconds(30)").Where(path => path.Contains("Supervision", StringComparison.Ordinal)));
        Assert.DoesNotContain(SourcesContaining(root, "StaleAfter"), path => path.StartsWith("DevalCopilot.Api/", StringComparison.Ordinal));
    }
}
