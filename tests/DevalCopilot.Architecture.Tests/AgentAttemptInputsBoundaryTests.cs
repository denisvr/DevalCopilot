using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0051: the recorded-input inspection is one read-only mediator query that reads untracked scalar projections of exactly one
/// attempt (through the shared ADR-0037 identity projection and evaluation), of a bounded number of its input rows and of the messages
/// they reference, never message text or unrelated attempt data, follows no chain, swallows no fault and uses no raw SQL in
/// Application.</summary>
public sealed partial class AgentAttemptInputsBoundaryTests
{
    private const string Folder = "DevalCopilot.Application/Features/Runs/Queries/GetAgentAttemptInputs";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetAgentAttemptInputs/GetAgentAttemptInputsEndpoint.cs";
    private const string SharedIdentity = "DevalCopilot.Application/Features/Runs/ScalarAgentAttemptIdentity.cs";

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

    private static string BackendCode(string relative) => Code(Path.Combine(BackendRoot(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string QueryCode()
    {
        var folder = Path.Combine(BackendRoot(), Folder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(folder), "The recorded-input inspection must live in its own operation folder.");
        return string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Order(StringComparer.Ordinal).Select(Code));
    }

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [Fact]
    public void The_query_only_reads_untracked_attempts_inputs_and_messages_and_writes_or_reaches_nothing()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.Equal(
            ["AttemptInputMessages", "Attempts", "CollaborationMessages"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteSql", "FromSql", "SqlQuery", "Attach(", "System.IO", "Process", "Http",
            "Task.Run", "IClock", ".Include(", "Artifacts", "Events", "Projects", "GitCheckpoints", "dbContext.Runs",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // The only in-memory collection writes are to the local reference set and dictionary; no DbSet is written or attached.
        Assert.DoesNotMatch(@"\.(Attempts|AttemptInputMessages|CollaborationMessages)\s*\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(", code);
        Assert.Single(Regex.Matches(code, @"\.Add\("));
        Assert.Contains("referenced.Add(inputs[index].CollaborationMessageId)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_text_and_unrelated_attempt_data_are_never_selected_and_no_entity_is_materialized()
    {
        var code = QueryCode() + '\n' + BackendCode(SharedIdentity);

        foreach (var hidden in new[]
        {
            "Summary", "StructuredContentJson", "InReplyToMessageId", "Provenance", "AgentProviderSessionId", "ProcessArguments",
            "ProcessExecutablePath", "ProcessWorkingDirectory", "ProcessApprovedRoot", "AgentContextManifestArtifactId", "AgentGitWorkspaceId",
            "AgentGitCheckpointId", "AgentResultGitCheckpointId", "AgentCheckpointFingerprintSha256", "AgentOutcome", "AgentInputTokens",
            "AgentOutputTokens", "AgentModelContextLimitsSnapshot", "AgentDirectHumanGuidance", "AgentAccountUsageDecisionSnapshot",
            "AgentRepairSourceAttemptId", "AgentTimeout", "AgentProcess",
        })
        {
            Assert.DoesNotContain(hidden, code, StringComparison.Ordinal);
        }

        foreach (var forbidden in new[] { "ToListAsync", "SingleAsync", "SingleOrDefaultAsync", "AgentAttemptRead", "TryMaterializeAsync", "GetAssignmentSnapshot" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // Every read ends in a scalar projection: the attempt through the shared identity projection, inputs and messages through their own.
        Assert.Single(Regex.Matches(code, @"\.ProjectScalarIdentity\(\)\s*\.FirstOrDefaultAsync\("));
        Assert.Single(Regex.Matches(code, @"\.ProjectInput\(\)\s*\.ToArrayAsync\("));
        Assert.Single(Regex.Matches(code, @"\.ProjectReferencedMessage\(\)\s*\.ToArrayAsync\("));
        Assert.Equal(3, Regex.Matches(code, @"Async\(cancellationToken\)").Count);
    }

    [Fact]
    public void The_input_set_is_bounded_ordered_and_resolved_only_inside_the_run_without_chains_or_substitutes()
    {
        var code = QueryCode();

        Assert.Contains("public const int PositionBound = 64;", code, StringComparison.Ordinal);
        Assert.Contains("public const int MaximumInspectedInputs = AgentAttemptInputsProjection.PositionBound;", code, StringComparison.Ordinal);
        Assert.Contains("public const int MaximumShownInputs = 10;", code, StringComparison.Ordinal);
        Assert.Contains(".Where(input => input.AttemptId == query.AttemptId)", code, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(input => input.Sequence)", code, StringComparison.Ordinal);
        Assert.Contains(".Take(MaximumInspectedInputs + 1)", code, StringComparison.Ordinal);
        Assert.Contains("candidate.RunId == query.RunId && candidate.Id == query.AttemptId", code, StringComparison.Ordinal);
        Assert.Contains("message.RunId == query.RunId && referencedIds.Contains(message.Id)", code, StringComparison.Ordinal);
        Assert.Contains("input.Sequence % PositionBound == input.Sequence", code, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Skip(", "LastOrDefault", "Last(", "Max(", "Min(", "InReplyTo", "RepairSource" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Identity_is_decided_only_by_the_shared_evaluation_and_a_fault_is_never_swallowed()
    {
        var code = QueryCode();

        Assert.Single(Regex.Matches(code, @"ScalarAgentAttemptIdentity\.Evaluate\("));
        Assert.DoesNotContain("AgentAttemptIdentity.IsCoherent", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentAssignmentSnapshotPolicy", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.Parse", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.TryParse", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"Error\.NotFound\("));
        Assert.DoesNotContain("Error.Conflict(", code, StringComparison.Ordinal);
        Assert.Contains("agent_attempt_inputs.not_found", code, StringComparison.Ordinal);

        var shared = BackendCode(SharedIdentity);
        Assert.Single(Regex.Matches(shared, @"AgentAssignmentSnapshotPolicy\.Read\("));
        Assert.Single(Regex.Matches(shared, @"AgentAttemptIdentity\.IsCoherent\("));
        Assert.DoesNotContain("catch", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_is_one_bodyless_authorized_get_through_the_mediator()
    {
        var code = BackendCode(Endpoint);

        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.Contains("[HttpGet(\"{runId:guid}/agent-attempts/{attemptId:guid}/inputs\")]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IDevalCopilotDbContext", code, StringComparison.Ordinal);
        Assert.Contains("CollaborationSequence.ToString(CultureInfo.InvariantCulture)", code, StringComparison.Ordinal);
        Assert.Contains("OccurredAtUtc.UtcDateTime.ToString(\"O\", CultureInfo.InvariantCulture)", code, StringComparison.Ordinal);
    }
}
