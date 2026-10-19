using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0053: the selected input-message read is one read-only mediator query that proves the whole input set through the same
/// feature-owned policy as the reference inspection, then selects exactly one member's text through the unchanged ADR-0035/0036
/// projection and admission. It calls no other handler, adds no parser or raw SQL, swallows no fault and writes nothing.</summary>
public sealed partial class AgentAttemptInputMessageBoundaryTests
{
    private const string Folder = "DevalCopilot.Application/Features/Runs/Queries/GetAgentAttemptInputMessage";
    private const string Handler = Folder + "/GetAgentAttemptInputMessageQueryHandler.cs";
    private const string Endpoint = "DevalCopilot.Api/Features/Runs/GetAgentAttemptInputMessage/GetAgentAttemptInputMessageEndpoint.cs";
    private const string ApiFolder = "DevalCopilot.Api/Features/Runs/GetAgentAttemptInputMessage";

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

    private static string FolderCode(string relative)
    {
        var folder = Path.Combine(BackendRoot(), relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(folder), "The input-message read must live in its own operation folder.");
        return string.Join('\n', Directory.EnumerateFiles(folder, "*.cs").Order(StringComparer.Ordinal).Select(Code));
    }

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [Fact]
    public void The_set_is_proved_by_the_shared_policy_before_exactly_one_untracked_message_read()
    {
        var code = BackendCode(Handler);

        Assert.Single(Regex.Matches(code, @"RecordedAgentAttemptInputSetReader\.ReadAsync\("));
        Assert.Equal(["CollaborationMessages"], DbSetAccess().Matches(code).Select(match => match.Groups[1].Value));
        Assert.Contains("dbContext.CollaborationMessages.AsNoTracking()", code, StringComparison.Ordinal);
        Assert.Contains(".Where(message => message.RunId == query.RunId && message.Id == selected.CollaborationMessageId)", code, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(code, @"\.Project\(\)\s*\.FirstOrDefaultAsync\(cancellationToken\)"));
        Assert.Single(Regex.Matches(code, @"CollaborationHistoryRows\.ToEntry\("));

        // The proof, then membership, then the one content read, in that order.
        var proof = code.IndexOf("RecordedAgentAttemptInputSetReader.ReadAsync(", StringComparison.Ordinal);
        var membership = code.IndexOf("set.Inputs.FirstOrDefault(", StringComparison.Ordinal);
        var content = code.IndexOf("dbContext.CollaborationMessages", StringComparison.Ordinal);
        Assert.True(proof >= 0 && proof < membership && membership < content);
    }

    [Fact]
    public void The_operation_reuses_the_existing_admission_and_adds_no_parser_identity_rule_or_handler_composition()
    {
        var code = FolderCode(Folder);

        foreach (var forbidden in new[]
        {
            "SaveChanges", "ExecuteSql", "FromSql", "SqlQuery", "Attach(", "System.IO", "Process", "Http", "Task.Run", "IClock", ".Include(",
            "Artifacts", "Events", "Projects", "GitCheckpoints", "dbContext.Runs", "dbContext.Attempts", "dbContext.AttemptInputMessages",
            "JsonDocument", "JsonSerializer", "CollaborationMessageContentPolicy", "ScalarAgentAttemptIdentity", "ProjectScalarIdentity",
            "IApplicationMediator", "IQueryHandler<GetAgentAttemptInputs", "InReplyTo", "RepairSource", "AgentProviderSessionId", "StructuredContentJson",
            "Enum.Parse", "Enum.TryParse", "catch", "ToListAsync", "ToArrayAsync", "Skip(",
        })
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(@"new\s+\w+QueryHandler\(", code);
        Assert.DoesNotMatch(@"\.HandleAsync\(", code);
        Assert.Single(Regex.Matches(code, @"Error\.NotFound\("));
        Assert.DoesNotContain("Error.Conflict(", code, StringComparison.Ordinal);
        Assert.Contains("agent_attempt_input_message.not_found", code, StringComparison.Ordinal);
        // A reread header must equal the admitted reference; nothing is substituted.
        Assert.Contains("row.OccurredAtUtc.EqualsExact(selected.OccurredAtUtc)", code, StringComparison.Ordinal);
        Assert.Contains("row.Sequence != selected.CollaborationSequence", code, StringComparison.Ordinal);
        Assert.Contains("CollaborationHistoryRows.TypeOf(row.TypeCode) != selected.Type", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_endpoint_is_one_bodyless_authorized_get_through_the_mediator_with_string_sequences_and_times()
    {
        var code = BackendCode(Endpoint);

        Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
        Assert.Contains("[HttpGet(\"{runId:guid}/agent-attempts/{attemptId:guid}/inputs/{messageId:guid}\")]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
        Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
        Assert.Contains("IApplicationMediator", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IDevalCopilotDbContext", code, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(code, @"Sequence\.ToString\(CultureInfo\.InvariantCulture\)").Count);
        Assert.Equal(2, Regex.Matches(code, @"OccurredAtUtc\.UtcDateTime\.ToString\(""O"", CultureInfo\.InvariantCulture\)").Count);
        // The API owns the whole response graph: no history or reference DTO is reused.
        Assert.DoesNotContain("CollaborationHistoryEntryMapper", code, StringComparison.Ordinal);
        Assert.DoesNotContain("CollaborationHistoryEntryResponse", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentAttemptInputReferenceResponse", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset", FolderCode(ApiFolder), StringComparison.Ordinal);
    }
}
