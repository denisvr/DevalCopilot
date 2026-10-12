using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0036: the reply inspection is one read-only mediator query that resolves at most one stored parent by exact identity
/// within the run, decides the pair only with the existing Domain reply policy, never follows a chain, loops or swallows a fault, and
/// discloses the parent only through the shared ADR-0035 projection and entry builder.</summary>
public sealed partial class CollaborationReplyBoundaryTests
{
    private const string Folder = "DevalCopilot.Application/Features/Runs/Queries/GetCollaborationHistoryReply";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetCollaborationHistoryReply/GetCollaborationHistoryReplyEndpoint.cs";

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
        Assert.True(Directory.Exists(folder), "The reply-inspection slice must live in its own operation folder.");
        return string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Order(StringComparer.Ordinal).Select(Code));
    }

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    [Fact]
    public void The_query_only_reads_untracked_runs_and_messages_and_writes_or_reaches_nothing()
    {
        var code = QueryCode();

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.Equal(
            ["CollaborationMessages", "Runs"], DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in new[]
        {
            "SaveChanges", ".Add(", ".Remove(", ".Update(", "ExecuteSql", "FromSql", "Attach(", "System.IO", "Process", "Http", "Task.Run",
            "IClock", "Provider",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void At_most_one_parent_is_resolved_by_its_stored_identity_without_loops_chains_or_a_substitute()
    {
        var code = QueryCode();

        Assert.Equal(2, Regex.Matches(code, @"FirstOrDefaultAsync\(").Count);
        Assert.Contains("message.RunId == runId && message.Id == parentId", code, StringComparison.Ordinal);
        Assert.Contains("message.Sequence < source.Sequence", code, StringComparison.Ordinal);
        Assert.Contains("message.ProtocolVersion == version", code, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "while (", "for (", "foreach (", "do\n", "Take(", "Skip(", "OrderBy", "LastOrDefault", "Last(", "First(" })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_pair_is_decided_only_by_the_domain_reply_policy_and_a_fault_is_never_swallowed()
    {
        var code = QueryCode();

        Assert.Single(Regex.Matches(code, @"CollaborationMessageReplyPolicy\.Evaluate\("));
        Assert.Single(Regex.Matches(code, @"CollaborationMessageReplyPolicy\.EvaluateReference\("));
        Assert.DoesNotContain("catch", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.Parse", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.TryParse", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\.Project\(\)"));
        Assert.Single(Regex.Matches(code, @"CollaborationHistoryRows\.ToEntry\("));
        Assert.DoesNotContain("\"Summary\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StructuredContentJson", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"Error\.Conflict\("));
        Assert.Single(Regex.Matches(code, @"Error\.NotFound\("));
        Assert.Contains("collaboration_reply.unavailable", code, StringComparison.Ordinal);
        Assert.Contains("collaboration_reply.not_found", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_is_one_bodyless_authorized_get_that_maps_only_through_the_shared_entry_mapper()
    {
        var code = Code(Path.Combine(BackendRoot(), Endpoint.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.Contains("[HttpGet(\"{runId:guid}/collaboration-history/{messageId:guid}/reply\")]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator", code, StringComparison.Ordinal);
        Assert.Contains("CollaborationHistoryEntryMapper.Map(parent)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IDevalCopilotDbContext", code, StringComparison.Ordinal);
    }
}
