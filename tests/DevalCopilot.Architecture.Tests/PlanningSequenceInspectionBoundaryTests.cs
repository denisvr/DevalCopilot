using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0068: the recorded planning sequence inspection is two read-only mediator queries over bounded untracked scalar
/// projections and one shared structural reader. They never invoke the live planning status, arming availability, classifier, lineage,
/// admission, fence, dispatch, recorder or mutation handler, read no latest or current row, message content, Git, filesystem, process,
/// provider or account state, and reach no artifact store. The two endpoints are authorized bodyless MVC GETs that dispatch through the
/// mediator and answer with API-owned graphs.</summary>
public sealed partial class PlanningSequenceInspectionBoundaryTests
{
    private static readonly string[] ApplicationFolders =
    [
        "DevalCopilot.Application/Features/Runs/Queries/GetPlanningSequenceInspection",
        "DevalCopilot.Application/Features/Runs/Queries/GetPlanningStepInspection",
        "DevalCopilot.Application/Features/Runs/Policies/PlanningSequenceInspection",
    ];

    private static readonly string[] ApiFolders =
    [
        "DevalCopilot.Api/Features/Runs/GetPlanningSequenceInspection",
        "DevalCopilot.Api/Features/Runs/GetPlanningStepInspection",
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

    private static string Code(string path) => string.Join(
        '\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

    private static string[] Sources(IEnumerable<string> folders) => folders
        .SelectMany(folder =>
        {
            var path = Path.Combine(BackendRoot(), folder.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(Directory.Exists(path), $"{folder} must exist as its own operation folder.");
            return Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories);
        })
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static string ApplicationCode() => string.Join('\n', Sources(ApplicationFolders).Select(Code));

    private static string ApiCode() => string.Join('\n', Sources(ApiFolders).Select(Code));

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(")]
    private static partial Regex DbSetWrite();

    [Fact]
    public void The_application_slice_only_reads_untracked_rows_of_the_recorded_tables_and_writes_nothing()
    {
        var code = ApplicationCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.DoesNotMatch(DbSetWrite(), code);
        Assert.Equal(
            ["Attempts", "CollaborationMessages", "GitCheckpoints", "GitWorkspaces", "PlanningSequenceSteps", "PlanningSequences", "Runs"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction", "IsModified", "AsTracking", "TimeProvider",
            "IRunEventNotifier", "RunEvent.Record", "IApplicationMediator", "IGit", "ILocalCommit", "IProcess", "ProcessStart", "IAgent",
            "IAttemptDurabilityProbe", "IPlanningSequenceStepProbe", "IArtifactStore", "File.", "Directory.", "Path.Combine", "Path.GetFull",
            "HttpClient", "Task.Delay", "Include(", "ThenInclude(", "FromSql", "ExecuteSql", "Environment.", "Process.",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_live_status_arming_classifier_lineage_fence_dispatch_or_ownership_policy_is_consulted_and_nothing_reads_a_latest_row()
    {
        var code = ApplicationCode();

        foreach (var forbidden in new[]
        {
            "PlanningSequenceStepAuthority", "PlanningSequenceFence", "PlanningSequenceDispatchGate", "PlanningSequenceOutcomeClassifier",
            "PlanningSequenceNarrative", "PlanningSequenceObjective", "PlanningSequencePinnedSource", "PlanningSequenceEvents",
            "PlanningLineage", "GetPlanningSequenceQuery", "PlanningSequenceManualFence", "PlanningSequenceArmingAvailability",
            "PlanningSequenceFinalReferences", "WorkspaceExecution", "ExecutionCapacity", "RepositoryMutationLeases", "LocalExecution",
            "Commands.", "CommandHandler", "OrderByDescending", "MaxAsync", "MinAsync", "LastOrDefault", "Latest", "Current",
            "StructuredContentJson", "InReplyToMessageId", "AttemptInputMessages", "AttemptOutputs", "VerificationExecution",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_fault_is_swallowed_and_every_scalar_read_is_a_bounded_projection_never_a_whole_entity_or_an_unbounded_list()
    {
        var code = ApplicationCode();

        Assert.DoesNotContain("catch", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ToListAsync<", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)\s*\.ToListAsync", code);
        Assert.Contains("StepBound = PlanningSequencePolicy.MaximumSteps + 1", code, StringComparison.Ordinal);
        Assert.Contains(".Take(PlanningSequenceInspectionProjection.StepBound)", code, StringComparison.Ordinal);
        Assert.Contains("MaximumSafeInteger = (1L << 53) - 1", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PreparationOwnerToken", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ObjectiveSha256", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DetailCode", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkspacePath", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Summary", code, StringComparison.Ordinal);

        // Every identifier and time is projected as bounded raw bytes and admitted by the one storage parser: no row record can hold a
        // typed Guid or DateTimeOffset, so no projection can materialize one.
        foreach (var rowName in Directory.EnumerateFiles(
            Path.Combine(BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Policies", "PlanningSequenceInspection"), "*Row.cs"))
        {
            var row = Code(rowName);
            Assert.DoesNotContain("Guid ", row, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTimeOffset", row, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_structure_is_proved_before_the_association_and_the_revision_is_rechecked_after_each_proof()
    {
        var step = Code(Path.Combine(
            BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Queries", "GetPlanningStepInspection",
            "GetPlanningStepInspectionQueryHandler.cs"));
        var reader = Code(Path.Combine(
            BackendRoot(), "DevalCopilot.Application", "Features", "Runs", "Policies", "PlanningSequenceInspection",
            "PlanningSequenceInspectionReader.cs"));

        var structure = step.IndexOf("PlanningSequenceInspectionReader.ReadAsync(", StringComparison.Ordinal);
        var membership = step.IndexOf("PlanningSequenceInspectionReader.HasStepAsync(", StringComparison.Ordinal);
        var association = step.IndexOf("PlanningSequenceInspectionAgentProof.ProveAsync(", StringComparison.Ordinal);
        var noLinkRecheck = step.IndexOf("PlanningSequenceInspectionReader.IsRevisionUnchangedAsync(", StringComparison.Ordinal);
        var recheck = step.LastIndexOf("PlanningSequenceInspectionReader.IsRevisionUnchangedAsync(", StringComparison.Ordinal);
        var noLink = step.IndexOf("if (step.AttemptId is null)", StringComparison.Ordinal);
        Assert.True(structure >= 0 && structure < membership && membership < association && association < recheck);

        // The successful no-link answer is compared after the membership read, and the linked answer after its association proof.
        Assert.True(membership < noLink && noLink < noLinkRecheck && noLinkRecheck < association);
        Assert.Equal(2, Regex.Matches(step, @"IsRevisionUnchangedAsync\(").Count);
        Assert.Contains("PlanningSequenceInspectionAgentProof.ProveAsync(", step, StringComparison.Ordinal);
        var steps = reader.IndexOf(".ProjectStep()", StringComparison.Ordinal);
        var after = reader.IndexOf("return await IsRevisionUnchangedAsync(", StringComparison.Ordinal);
        Assert.True(steps >= 0 && steps < after, "The structural revision is compared after every bounded structural read.");
    }

    [Fact]
    public void The_two_endpoints_dispatch_only_through_the_mediator_are_bodyless_gets_and_answer_with_api_owned_graphs()
    {
        var code = ApiCode();
        var endpoints = Sources(ApiFolders).Where(path => path.EndsWith("Endpoint.cs", StringComparison.Ordinal)).ToArray();

        Assert.Equal(2, endpoints.Length);
        foreach (var path in endpoints)
        {
            var source = Code(path);
            Assert.Contains(": RunsBaseEndpoint", source, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(source, @"\[HttpGet\("));
            Assert.Single(Regex.Matches(source, @"mediator\.SendAsync\("));
            Assert.Contains("[TypeFilter(typeof(PlanningSequenceInspectionBodylessGuardFilter))]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("[FromBody]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("[AllowAnonymous]", source, StringComparison.Ordinal);
            Assert.DoesNotContain("DbContext", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("IArtifactStore", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeProvider", code, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"record \w+\([^)]*\b(PlanningSequenceInspection(Step|Header)|AgentAttemptVerificationEvidenceHeader)\b[^)]*\)", code);
        foreach (var path in Sources(ApiFolders).Where(path => path.EndsWith("Response.cs", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("DevalCopilot.Application", Code(path), StringComparison.Ordinal);
            Assert.DoesNotContain("DevalCopilot.Domain", Code(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_body_and_query_guard_runs_before_binding_and_admits_only_the_documented_shapes()
    {
        var guard = Code(Path.Combine(
            BackendRoot(), "DevalCopilot.Api", "Features", "Runs", "GetPlanningSequenceInspection",
            "PlanningSequenceInspectionBodylessGuardFilter.cs"));

        Assert.Contains(": IAsyncResourceFilter", guard, StringComparison.Ordinal);
        Assert.Contains("request.Query.Count > 0", guard, StringComparison.Ordinal);
        Assert.Contains("request.ContentLength is > 0", guard, StringComparison.Ordinal);
        Assert.Contains("HeaderNames.TransferEncoding", guard, StringComparison.Ordinal);
        Assert.Contains("request.Body.ReadAsync(new byte[1]", guard, StringComparison.Ordinal);
        Assert.Contains("PlanningSequenceInspectionErrors.QueryNotAllowed()", guard, StringComparison.Ordinal);
        Assert.Contains("PlanningSequenceInspectionErrors.BodyNotAllowed()", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("mediator", guard, StringComparison.OrdinalIgnoreCase);
    }
}
