using System.Text.RegularExpressions;
using Xunit;

namespace DevalCopilot.Architecture.Tests;

/// <summary>ADR-0052: the recorded-verification inspection is two read-only mediator queries that share one deliberately co-evolving
/// read policy. The policy reads untracked scalar projections of exactly one attempt, its recorded checkpoint, at most 33 memberships and
/// exactly their executions, through the unchanged shared identity evaluation; the list never reads an artifact path, hash or byte; the
/// window reaches bytes only through the artifact store's sealed verification; neither consults a current recipe, eligibility, applicability
/// or authority reader, reads a membership digest, writes, or swallows a fault, and Application uses no raw SQL.</summary>
public sealed partial class AgentAttemptVerificationBoundaryTests
{
    private const string EvidenceFolder = "DevalCopilot.Application/Features/Runs/Queries/GetAgentAttemptVerificationEvidence";
    private const string WindowFolder = "DevalCopilot.Application/Features/Runs/Queries/GetAgentAttemptVerificationOutputWindow";
    private const string PolicyFolder = "DevalCopilot.Application/Features/Runs/Policies/AgentAttemptVerification";
    private const string EvidenceEndpoint = "DevalCopilot.Api/Features/Runs/GetAgentAttemptVerificationEvidence/GetAgentAttemptVerificationEvidenceEndpoint.cs";
    private const string WindowEndpoint = "DevalCopilot.Api/Features/Runs/GetAgentAttemptVerificationOutputWindow/GetAgentAttemptVerificationOutputWindowEndpoint.cs";
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

    private static string FolderCode(string folder)
    {
        var path = Path.Combine(BackendRoot(), folder.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(Directory.Exists(path), $"{folder} must exist as its own operation or policy folder.");
        return string.Join('\n', Directory.EnumerateFiles(path, "*.cs").Order(StringComparer.Ordinal).Select(Code));
    }

    private static string FileCode(string folder, string file) => BackendCode($"{folder}/{file}");

    [GeneratedRegex(@"dbContext\.([A-Za-z]+)")]
    private static partial Regex DbSetAccess();

    [GeneratedRegex(@"dbContext\.[A-Za-z]+\.AsNoTracking\(\)")]
    private static partial Regex UntrackedDbSetAccess();

    private static readonly string[] ForbiddenEverywhere =
    [
        "SaveChanges", "ExecuteSql", "FromSql", "SqlQuery", "Attach(", "System.Diagnostics", "Process.Start", "Http", "Task.Run", "IClock", ".Include(", "catch",
        "Enum.Parse", "Enum.TryParse", "SnapshotSha256", "VerificationCommands", "VerificationDiagnosisApplicability",
        "VerificationDiagnosisEvidence", "VerificationDiagnosisSnapshot", "VerificationDiagnosisEligibility", "LocalCommit",
        "CodeReviewInputIdentity", "AgentAttemptRead", "TryMaterializeAsync", "ToListAsync", "SingleAsync", "SingleOrDefaultAsync",
        "dbContext.Events", "dbContext.Artifacts", "dbContext.GitChangedFiles", "WorkspacePath", "ExecutablePath", "TimeoutSeconds", ".Arguments", "Lease",
    ];

    [Fact]
    public void The_reads_only_touch_the_named_untracked_sets_and_write_or_reach_nothing()
    {
        var code = FolderCode(PolicyFolder) + '\n' + FolderCode(EvidenceFolder) + '\n' + FolderCode(WindowFolder);

        Assert.Equal(DbSetAccess().Matches(code).Count, UntrackedDbSetAccess().Matches(code).Count);
        Assert.Equal(
            [
                "AttemptVerificationEvidence", "Attempts", "GitCheckpoints", "GitWorkspaces", "Runs", "VerificationExecutions",
                "VerificationOutputArtifacts",
            ],
            DbSetAccess().Matches(code).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal));
        foreach (var forbidden in ForbiddenEverywhere)
        {
            Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
        }

        Assert.DoesNotMatch(
            @"\.(Attempts|Runs|GitCheckpoints|GitWorkspaces|AttemptVerificationEvidence|VerificationExecutions|VerificationOutputArtifacts)\s*\.(Add|AddRange|Remove|RemoveRange|Update|UpdateRange|Attach)\(",
            code);
    }

    [Fact]
    public void Every_read_ends_in_a_scalar_projection_and_no_entity_or_unguarded_column_is_materialized()
    {
        var policy = FolderCode(PolicyFolder);
        var evidence = FolderCode(EvidenceFolder);
        var window = FolderCode(WindowFolder);
        var all = policy + '\n' + evidence + '\n' + window;

        Assert.Single(Regex.Matches(policy, @"\.ProjectScalarIdentity\(\)\s*\.FirstOrDefaultAsync\("));
        Assert.Single(Regex.Matches(policy, @"\.ProjectVerificationOwnership\(dbContext\.Runs\.AsNoTracking\(\)\)\s*\.FirstOrDefaultAsync\("));
        Assert.Single(Regex.Matches(policy, @"\.ProjectCheckpointOwnership\(dbContext\.GitWorkspaces\.AsNoTracking\(\)\)\s*\.FirstOrDefaultAsync\("));
        Assert.Single(Regex.Matches(policy, @"\.ProjectMembership\(\)\s*\.ToArrayAsync\("));
        Assert.Single(Regex.Matches(policy, @"\.ProjectExecution\(\)\s*\.ToArrayAsync\("));
        Assert.Single(Regex.Matches(evidence, @"\.ProjectDescriptor\(\)\s*\.ToArrayAsync\("));
        Assert.Single(Regex.Matches(window, @"\.ProjectStorage\(\)\s*\.ToArrayAsync\("));

        // Every database round trip is one of the above; nothing else is awaited against the database.
        Assert.Equal(5, Regex.Matches(policy, @"Async\(cancellationToken\)").Count);
        Assert.Single(Regex.Matches(evidence, @"Async\(cancellationToken\)"));
        Assert.Single(Regex.Matches(window, @"Async\(cancellationToken\)"));

        // Every guarded string is read through its byte guard and the stored paths and hashes exist only in the window's storage row.
        var projection = FileCode(PolicyFolder, "RecordedVerificationProjection.cs");
        var descriptor = projection[projection.IndexOf("ProjectDescriptor(", StringComparison.Ordinal)..projection.IndexOf("ProjectStorage(", StringComparison.Ordinal)];
        Assert.DoesNotContain("RelativeStoragePath", descriptor, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentHash", descriptor, StringComparison.Ordinal);
        foreach (var guarded in new[] { "CommandName", "CheckpointFingerprintSha256", "CompletionFingerprintSha256", "FingerprintSha256", "RelativeStoragePath", "ContentHash" })
        {
            Assert.Contains($"(byte[])(object)", projection, StringComparison.Ordinal);
            Assert.Matches($@"\(byte\[\]\)\(object\)[A-Za-z.]*\.{guarded}!?\)\.Length", projection);
        }

        Assert.DoesNotContain("RelativeStoragePath", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentHash", evidence, StringComparison.Ordinal);
        Assert.DoesNotContain("IArtifactStore", evidence + policy, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentGitWorkspaceId = ", all, StringComparison.Ordinal);
    }

    [Fact]
    public void The_membership_is_bounded_ordered_and_resolved_only_by_recorded_identity()
    {
        var policy = FolderCode(PolicyFolder);

        Assert.Contains("public const int PositionBound = 32;", policy, StringComparison.Ordinal);
        Assert.Contains("public const int MaximumInspectedMembers = RecordedVerificationProjection.PositionBound;", policy, StringComparison.Ordinal);
        Assert.Contains(".Where(membership => membership.AttemptId == attemptId)", policy, StringComparison.Ordinal);
        Assert.Contains(".OrderBy(membership => membership.Sequence)", policy, StringComparison.Ordinal);
        Assert.Contains(".Take(MaximumInspectedMembers + 1)", policy, StringComparison.Ordinal);
        Assert.Contains("candidate.RunId == runId && candidate.Id == attemptId", policy, StringComparison.Ordinal);
        Assert.Contains(".Where(candidate => candidate.Id == checkpointId)", policy, StringComparison.Ordinal);
        Assert.Contains(".Where(execution => referencedIds.Contains(execution.Id))", policy, StringComparison.Ordinal);
        Assert.Contains("membership.Sequence % PositionBound == membership.Sequence", policy, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Skip(", "LastOrDefault", "Last(", "Max(", "Min(", "OrderByDescending", "NextCheckpointNumber", "IsEnabled" })
        {
            Assert.DoesNotContain(forbidden, policy, StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(policy, @"ScalarAgentAttemptIdentity\.Evaluate\("));
        Assert.DoesNotContain("AgentAttemptIdentity.IsCoherent", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentAssignmentSnapshotPolicy", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_reads_no_artifact_bytes_and_the_window_reaches_them_only_through_the_sealed_store_call()
    {
        var evidence = FolderCode(EvidenceFolder);
        var window = FolderCode(WindowFolder);

        Assert.DoesNotContain("artifactStore", evidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.IO", evidence + window, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(window, @"artifactStore\.[A-Za-z]+\("));
        Assert.Single(Regex.Matches(window, @"artifactStore\.VerifyAndReadSealedAsync\("));
        Assert.Contains("storage.RelativeStoragePath!, byteLength, storage.ContentHash!, query.FromOffset, query.MaxBytes, cancellationToken", window, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "ReadPartialAsync", "GetPartialPath", "SealAsync", "DescribeSealedFileAsync", "DeleteOrphaned", "File.", "Directory.", "FileStream" })
        {
            Assert.DoesNotContain(forbidden, window, StringComparison.Ordinal);
        }

        // The requested stream is the only row read, and the handler enforces its bounds itself too.
        Assert.Contains("output.VerificationExecutionId == query.ExecutionId && output.Purpose == purpose", window, StringComparison.Ordinal);
        Assert.Contains(".Take(2)", window, StringComparison.Ordinal);
        Assert.Contains("query.FromOffset is < 0 or > GetAgentAttemptVerificationOutputWindowQuery.MaximumOffset", window, StringComparison.Ordinal);
        Assert.Contains("GetAgentAttemptVerificationOutputWindowQuery.MinimumMaxBytes", window, StringComparison.Ordinal);
        Assert.Contains("GetAgentAttemptVerificationOutputWindowQuery.MaximumMaxBytes", window, StringComparison.Ordinal);
        Assert.Contains("public const int MinimumMaxBytes = 64;", window, StringComparison.Ordinal);
        Assert.Contains("public const int MaximumMaxBytes = 64 * 1024;", window, StringComparison.Ordinal);
        Assert.Contains("public const long MaximumOffset = (1L << 53) - 1;", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Clamp", window + evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_admitted_content_hash_form_and_path_bounds_are_the_stores_exact_ones()
    {
        var admission = FileCode(PolicyFolder, "RecordedVerificationOutputAdmission.cs");

        Assert.Contains("private const string ContentHashPrefix = \"sha256:\";", admission, StringComparison.Ordinal);
        Assert.Contains("private const int ContentHashDigits = 64;", admission, StringComparison.Ordinal);
        Assert.Contains("IndexOfAnyExcept(\"0123456789abcdef\")", admission, StringComparison.Ordinal);
        Assert.Contains("value is { Length: 71 }", admission, StringComparison.Ordinal);
        Assert.Contains("MaximumRelativeStoragePathLength = 1024", admission, StringComparison.Ordinal);
        var projection = FileCode(PolicyFolder, "RecordedVerificationProjection.cs");
        Assert.Contains("RelativeStoragePathByteBound = 3072", projection, StringComparison.Ordinal);
        Assert.Contains("ContentHashByteBound = 71", projection, StringComparison.Ordinal);
        Assert.Contains("CommandNameByteBound = 600", projection, StringComparison.Ordinal);
        Assert.Contains("FingerprintByteBound = 64", projection, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_operations_fail_closed_with_the_one_shared_not_found_and_no_current_state_reader()
    {
        var all = FolderCode(PolicyFolder) + '\n' + FolderCode(EvidenceFolder) + '\n' + FolderCode(WindowFolder);

        Assert.Contains("agent_attempt_verification.not_found", all, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(FileCode(PolicyFolder, "AgentAttemptVerificationErrors.cs"), @"Error\.NotFound\("));
        Assert.DoesNotContain("Error.Conflict(", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Error.Failure(", all, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "ReadOnlyFormatRepair", "RunLifecycle", "WorkspaceStatus", "ReadyWorkspace", "CurrentCodex", "ActiveAttempt" })
        {
            Assert.DoesNotContain(forbidden, all, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_shared_identity_evaluation_is_unchanged_and_still_swallows_nothing()
    {
        var shared = BackendCode(SharedIdentity);

        Assert.Single(Regex.Matches(shared, @"AgentAssignmentSnapshotPolicy\.Read\("));
        Assert.Single(Regex.Matches(shared, @"AgentAttemptIdentity\.IsCoherent\("));
        Assert.DoesNotContain("catch", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("dbContext", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_endpoint_is_one_bodyless_authorized_get_through_the_mediator()
    {
        var evidence = BackendCode(EvidenceEndpoint);
        var window = BackendCode(WindowEndpoint);

        foreach (var code in new[] { evidence, window })
        {
            Assert.Single(Regex.Matches(code, @"\[Http[A-Za-z]+\("));
            Assert.DoesNotContain("[FromBody]", code, StringComparison.Ordinal);
            Assert.DoesNotContain("AllowAnonymous", code, StringComparison.Ordinal);
            Assert.Contains(": RunsBaseEndpoint", code, StringComparison.Ordinal);
            Assert.Contains("IApplicationMediator", code, StringComparison.Ordinal);
            Assert.DoesNotContain("IDevalCopilotDbContext", code, StringComparison.Ordinal);
            Assert.DoesNotContain("IArtifactStore", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Math.Clamp", code, StringComparison.Ordinal);
        }

        Assert.Contains("[HttpGet(\"{runId:guid}/agent-attempts/{attemptId:guid}/verification-evidence\")]", evidence, StringComparison.Ordinal);
        Assert.Contains(
            "[HttpGet(\"{runId:guid}/agent-attempts/{attemptId:guid}/verification-evidence/{executionId:guid}/artifact-window/{stream}\")]",
            window,
            StringComparison.Ordinal);
        Assert.Contains("\"stdout\" => VerificationOutputPurpose.StandardOutput", window, StringComparison.Ordinal);
        Assert.Contains("\"stderr\" => VerificationOutputPurpose.StandardError", window, StringComparison.Ordinal);
        Assert.Contains("value.UtcDateTime.ToString(\"O\", CultureInfo.InvariantCulture)", evidence, StringComparison.Ordinal);
    }
}
