using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0037: the attempt attribution is one read-only mediator query that reads untracked scalar projections of the source message
/// and of at most the one attempt it references, never message text or unrelated attempt data, judges the attempt only through the shared
/// assignment read policy and the unchanged identity rule, follows no chain and swallows no fault, and uses no raw SQL in Application.</summary>
public sealed partial class CollaborationAttemptBoundaryTests
{
    private const string Folder = "DevalCopilot.Application/Features/Runs/Queries/GetCollaborationHistoryAttempt";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetCollaborationHistoryAttempt/GetCollaborationHistoryAttemptEndpoint.cs";

    // ADR-0051 extracted the attempt projection and identity evaluation unchanged into these shared files; they are part of this read.
    private static readonly string[] SharedAttemptIdentity =
    [
        "DevalCopilot.Application/Features/Runs/ScalarAgentAttemptIdentity.cs",
        "DevalCopilot.Application/Features/Runs/ScalarAgentAttemptRow.cs",
        "DevalCopilot.Application/Features/Runs/CoherentAgentAttemptIdentity.cs",
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

    private static string QueryCode()
    {
        var folder = Path.Combine(BackendRoot(), Folder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(folder), "The attempt-attribution slice must live in its own operation folder.");
        var shared = SharedAttemptIdentity.Select(path => Path.Combine(BackendRoot(), path.Replace('/', Path.DirectorySeparatorChar)));
        return string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Order(StringComparer.Ordinal).Concat(shared).Select(Code));
    }

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [Fact]
    public void The_query_only_reads_untracked_runs_messages_and_attempts_and_writes_or_reaches_nothing()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.Equal(
            ["Attempts", "CollaborationMessages", "Runs"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", ".Add(", ".Remove(", ".Update(", "ExecuteSql", "FromSql", "SqlQuery", "Attach(", "System.IO", "Process", "Http",
            "Task.Run", "IClock", ".Include(", "Artifacts", "Events", "Projects", "GitCheckpoints",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Message_text_and_unrelated_attempt_data_are_never_selected()
    {
        var code = QueryCode();

        foreach (var hidden in new[]
        {
            "message.Summary", "StructuredContentJson", "InReplyToMessageId", "AgentProviderSessionId", "ProcessArguments",
            "ProcessExecutablePath", "ProcessWorkingDirectory", "ProcessApprovedRoot", "AgentContextManifestArtifactId",
            "AgentGitWorkspaceId", "AgentGitCheckpointId", "AgentResultGitCheckpointId", "AgentCheckpointFingerprintSha256",
            "AgentOutcome", "AgentInputTokens", "AgentOutputTokens", "AgentModelContextLimitsSnapshot", "AgentDirectHumanGuidance",
            "AgentAccountUsageDecisionSnapshot", "AgentRepairSourceAttemptId", "AgentTimeout", "AgentProcess",
        })
        {
            Assert.DoesNotContain(hidden, code, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("ToListAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SingleAsync", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentAttemptRead", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TryMaterializeAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void At_most_one_attempt_is_resolved_by_the_messages_own_identity_without_loops_chains_or_a_substitute()
    {
        var code = QueryCode();

        Assert.Equal(2, Regex.Matches(code, @"FirstOrDefaultAsync\(").Count);
        Assert.Contains("candidate.RunId == runId && candidate.Id == attemptId", code, StringComparison.Ordinal);
        Assert.Contains("message.RunId == query.RunId && message.Id == query.MessageId", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\.ProjectScalarIdentity\(\)"));
        Assert.Single(Regex.Matches(code, @"\.ProjectSource\(\)"));
        foreach (var forbidden in new[] { "while (", "for (", "foreach (", "do\n", "Take(", "Skip(", "OrderBy", "LastOrDefault", "Last(", "First(", "Max(", "Min(" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Identity_is_decided_only_by_the_shared_assignment_policy_and_the_unchanged_rule_and_a_fault_is_never_swallowed()
    {
        var code = QueryCode();

        Assert.Single(Regex.Matches(code, @"AgentAssignmentSnapshotPolicy\.Read\("));
        Assert.Single(Regex.Matches(code, @"AgentAttemptIdentity\.IsCoherent\("));
        Assert.DoesNotContain("GetAssignmentSnapshot", code, StringComparison.Ordinal);
        Assert.DoesNotContain("catch", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.Parse", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.TryParse", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ParticipantIdentity", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"Error\.Conflict\("));
        Assert.Single(Regex.Matches(code, @"Error\.NotFound\("));
        Assert.Contains("collaboration_attempt.unavailable", code, StringComparison.Ordinal);
        Assert.Contains("collaboration_attempt.not_found", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_assignment_validation_string_and_the_max_turn_storage_are_read_only_under_a_database_byte_guard()
    {
        var code = QueryCode();

        Assert.Contains("AgentAssignmentSnapshotPolicy.MaximumIdentifierLength * 3", code, StringComparison.Ordinal);
        Assert.Contains("const int MaxTurnsByteBound = 64;", code, StringComparison.Ordinal);
        foreach (var column in new[]
        {
            "AgentRequestedModel", "AgentObservedModel", "AgentRequestedEffort", "AgentObservedEffort", "AgentAdapterContractVersion",
        })
        {
            Assert.Contains($"((byte[])(object)attempt.{column}!).Length <= IdentifierByteBound ? attempt.{column} : null", code, StringComparison.Ordinal);
            Assert.Contains($"attempt.{column} != null && ((byte[])(object)attempt.{column}).Length > IdentifierByteBound", code, StringComparison.Ordinal);
        }

        Assert.Contains("Attempt.AgentRequestedMaxTurnsStorageProperty", code, StringComparison.Ordinal);
        Assert.Contains("MaxTurnsByteBound", code, StringComparison.Ordinal);
        Assert.Contains("StoredMaxTurnsOversized", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_is_one_bodyless_authorized_get_through_the_mediator_that_maps_a_minimal_header()
    {
        var code = Code(Path.Combine(BackendRoot(), Endpoint.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.Contains("[HttpGet(\"{runId:guid}/collaboration-history/{messageId:guid}/attempt\")]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IDevalCopilotDbContext", code, StringComparison.Ordinal);
    }
}
