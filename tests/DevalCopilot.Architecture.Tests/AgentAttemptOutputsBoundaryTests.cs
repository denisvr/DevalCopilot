using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0054: the recorded-output inspection is one read-only mediator query that reads untracked scalar projections of exactly
/// one attempt (through the shared ADR-0037 identity projection and evaluation) and of at most 65 of its message headers selected by the
/// message-level attempt reference, never message text or unrelated attempt data, follows no chain, swallows no fault and uses no raw SQL
/// in Application. The proof lives in one feature-owned policy shared with the selected-output read.</summary>
public sealed partial class AgentAttemptOutputsBoundaryTests
{
    private const string Folder = "DevalCopilot.Application/Features/Runs/Queries/GetAgentAttemptOutputs";
    private const string PolicyFolder = "DevalCopilot.Application/Features/Runs/Policies/AgentAttemptOutputs";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetAgentAttemptOutputs/GetAgentAttemptOutputsEndpoint.cs";
    private const string ApiFolder = "DevalCopilot.Api/Features/Runs/GetAgentAttemptOutputs";

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

    private static string FolderCode(string relative, string message)
    {
        var folder = Path.Combine(BackendRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(folder), message);
        return string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Order(StringComparer.Ordinal).Select(Code));
    }

    private static string QueryCode() =>
        FolderCode(Folder, "The recorded-output inspection must live in its own operation folder.") + '\n'
        + FolderCode(PolicyFolder, "The recorded output-set proof must live in its own feature-owned policy folder.");

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [Fact]
    public void The_query_only_reads_untracked_attempts_and_messages_and_writes_or_reaches_nothing()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.Equal(
            ["Attempts", "CollaborationMessages"],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteSql", "FromSql", "SqlQuery", "Attach(", "System.IO", "Process", "Http", "Task.Run", "IClock", ".Include(",
            "Artifacts", "Events", "Projects", "GitCheckpoints", "dbContext.Runs", "AttemptInputMessages",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        // The only in-memory collection write is to the local identity set; no DbSet is written or attached.
        Assert.DoesNotMatch(@"\.(Attempts|CollaborationMessages)\s*\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(", code);
        Assert.Single(Regex.Matches(code, @"\.Add\("));
        Assert.Contains("identifiers.Add(row.Id)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Message_text_and_unrelated_attempt_data_are_never_selected_and_no_entity_is_materialized()
    {
        var code = QueryCode() + '\n' + BackendCode("DevalCopilot.Application/Features/Runs/ScalarAgentAttemptIdentity.cs");

        foreach (var hidden in new[]
        {
            "Summary", "StructuredContentJson", "InReplyToMessageId", "Recipient", "AgentProviderSessionId", "ProcessArguments",
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

        // Every read ends in a scalar projection: the attempt through the shared identity projection, headers through their own.
        Assert.Single(Regex.Matches(code, @"\.ProjectScalarIdentity\(\)\s*\.FirstOrDefaultAsync\("));
        Assert.Single(Regex.Matches(code, @"\.ProjectOutput\(\)\s*\.ToArrayAsync\("));
        Assert.Equal(2, Regex.Matches(code, @"Async\(cancellationToken\)").Count);
    }

    [Fact]
    public void The_output_set_is_selected_by_the_attempt_reference_bounded_ordered_and_refused_whole_without_chains_or_substitutes()
    {
        var code = QueryCode();

        Assert.Contains("public const int MaximumInspectedOutputs = 64;", code, StringComparison.Ordinal);
        Assert.Contains("public const int MaximumInspectedOutputs = RecordedAgentAttemptOutputSetReader.MaximumInspectedOutputs;", code, StringComparison.Ordinal);
        Assert.Contains(".Where(message => message.AttemptId == attemptId)", code, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(message => message.Sequence)", code, StringComparison.Ordinal);
        Assert.Contains(".Take(MaximumInspectedOutputs + 1)", code, StringComparison.Ordinal);
        Assert.Contains("candidate.RunId == runId && candidate.Id == attemptId", code, StringComparison.Ordinal);
        // The association is selected before the run is judged, so a foreign association is refused, never filtered away.
        Assert.DoesNotContain("message.RunId == runId", code, StringComparison.Ordinal);
        Assert.Contains("row.RunId != runId", code, StringComparison.Ordinal);
        Assert.Contains("CollaborationMessageAuthorPolicy.AllowedMessageTypes(identity.Role)", code, StringComparison.Ordinal);
        Assert.Contains("attempt.DispatchedAtUtc is null", code, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Skip(", "LastOrDefault", "Last(", "Max(", "Min(", "Math.Clamp", "Math.Min", "InReplyTo", "RepairSource", "Limit" })
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
        Assert.Contains("agent_attempt_outputs.not_found", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_is_one_bodyless_authorized_get_through_the_mediator_with_string_sequences_and_times()
    {
        var code = BackendCode(Endpoint);

        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.Contains("[HttpGet(\"{runId:guid}/agent-attempts/{attemptId:guid}/outputs\")]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IDevalCopilotDbContext", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"CollaborationSequence\.ToString\(CultureInfo\.InvariantCulture\)"));
        Assert.Single(Regex.Matches(code, @"OccurredAtUtc\.UtcDateTime\.ToString\(""O"", CultureInfo\.InvariantCulture\)"));
        Assert.DoesNotContain("AgentAttemptInput", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset", FolderCode(ApiFolder, "The API operation must live in its own folder."), StringComparison.Ordinal);
    }
}
