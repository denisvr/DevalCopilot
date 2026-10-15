using DevalCopilot.Application.Features.Runs.Policies.GitHubBranchPublicationApproval;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// ADR-0048 correction R1: every identifier and time the reader projects from the approval row and its one event is admitted from its
/// bounded stored representation before it is parsed. A malformed, oversized, NUL-containing or non-text stored scalar is an incoherent
/// reading: it is never an EF conversion fault, never defaulted, clamped, normalized or repaired, and the restored original reads back
/// coherent again. Each named case runs on its own fresh file-backed SQLite database with the production migrations.
/// </summary>
public sealed partial class GitHubBranchPublicationApprovalReaderTests
{
    private static readonly string[] ApprovalIdentifierColumns =
    [
        "Id", "EventId", "RunId", "ProjectId", "LocalCommitOperationId", "GitWorkspaceId", "TargetId", "TargetEventId", "BindingId",
        "BindingEventId",
    ];

    private static readonly string[] ApprovalTimeColumns =
        ["TargetRecordedAtUtc", "BindingObservedAtUtc", "BindingRecordedAtUtc", "ApprovedAtUtc"];

    private static readonly string[] IdentifierVariants =
        ["malformed", "oversized", "embedded-nul", "blob", "padded", "braced", "no-hyphens"];

    private static readonly string[] TimeVariants =
        ["malformed", "oversized-whitespace", "embedded-nul", "blob", "padded", "iso-t-separator"];

    public static TheoryData<string, string, string> StoredScalarCases()
    {
        var cases = new TheoryData<string, string, string>();
        foreach (var column in ApprovalIdentifierColumns)
        {
            foreach (var variant in IdentifierVariants)
            {
                cases.Add(Approvals, column, variant);
            }
        }

        foreach (var variant in IdentifierVariants)
        {
            cases.Add(Events, "Id", variant);
        }

        foreach (var column in ApprovalTimeColumns)
        {
            foreach (var variant in TimeVariants)
            {
                cases.Add(Approvals, column, variant);
            }
        }

        foreach (var variant in TimeVariants)
        {
            cases.Add(Events, "OccurredAtUtc", variant);
        }

        return cases;
    }

    private static string Corrupt(string column, string variant) => variant switch
    {
        "malformed" => $"\"{column}\" = 'not-a-stored-value'",
        "oversized" => $"\"{column}\" = \"{column}\" || replace(hex(zeroblob(30000)), '0', 'x')",
        "oversized-whitespace" => $"\"{column}\" = \"{column}\" || replace(hex(zeroblob(30000)), '0', ' ')",
        "embedded-nul" => $"\"{column}\" = \"{column}\" || char(0) || 'x'",
        "blob" => $"\"{column}\" = x'00112233445566778899aabbccddeeff'",
        "padded" => $"\"{column}\" = ' ' || \"{column}\" || ' '",
        "braced" => $"\"{column}\" = '{{' || \"{column}\" || '}}'",
        "no-hyphens" => $"\"{column}\" = replace(\"{column}\", '-', '')",
        "iso-t-separator" => $"\"{column}\" = replace(\"{column}\", ' ', 'T')",
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant, null),
    };

    private async Task<(Seeded Seeded, GitHubBranchPublicationApprovalView Original)> SeedCoherentAsync()
    {
        var seeded = await SeedAsync();
        await ApproveAsync(seeded);
        var control = await ReadAsync(seeded);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Coherent, control.Kind);
        return (seeded, Assert.IsType<GitHubBranchPublicationApprovalView>(control.Approval));
    }

    private async Task BackUpAsync()
    {
        await using var scratch = new SqliteConnection(_fixture.ConnectionString + ";Pooling=False");
        await scratch.OpenAsync();
        await using var backup = scratch.CreateCommand();
        backup.CommandText = $"CREATE TABLE bk_approvals AS SELECT * FROM {Approvals}; CREATE TABLE bk_events AS SELECT * FROM {Events};";
        await backup.ExecuteNonQueryAsync();
    }

    private Task RestoreAsync() => ExecuteAsync(
        $"DELETE FROM {Approvals}",
        $"INSERT INTO {Approvals} SELECT * FROM bk_approvals",
        $"DELETE FROM {Events}",
        $"INSERT INTO {Events} SELECT * FROM bk_events",
        "DROP TABLE bk_approvals",
        "DROP TABLE bk_events");

    [Theory]
    [MemberData(nameof(StoredScalarCases))]
    public async Task A_malformed_oversized_nul_containing_or_non_text_stored_scalar_is_incoherent_and_the_restored_original_is_coherent_again(
        string table, string column, string variant)
    {
        var (seeded, original) = await SeedCoherentAsync();
        var key = table == Approvals ? $"\"Id\" = '{Upper(original.Id)}'" : OwnEvent(seeded.Rows.Run.Id);
        await BackUpAsync();

        await ExecuteAsync($"UPDATE {table} SET {Corrupt(column, variant)} WHERE {key}");
        var broken = await ReadAsync(seeded);

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, broken.Kind);
        Assert.Null(broken.Approval);

        await RestoreAsync();
        var restored = await ReadAsync(seeded);
        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Coherent, restored.Kind);
        Assert.Equal(original, restored.Approval);
    }

    [Fact]
    public async Task A_malformed_approved_time_is_incoherent_and_never_an_ef_conversion_fault()
    {
        var (seeded, _) = await SeedCoherentAsync();
        await ExecuteAsync($"UPDATE {Approvals} SET \"ApprovedAtUtc\" = 'not-a-time'");

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, (await ReadAsync(seeded)).Kind);
    }

    [Fact]
    public async Task A_malformed_project_identifier_is_incoherent_and_never_an_ef_conversion_fault()
    {
        var (seeded, _) = await SeedCoherentAsync();
        await ExecuteAsync($"UPDATE {Approvals} SET \"ProjectId\" = 'not-a-guid'");

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, (await ReadAsync(seeded)).Kind);
    }

    [Fact]
    public async Task A_lowercase_stored_identifier_is_still_the_same_supported_identity_and_reads_coherent()
    {
        var (seeded, original) = await SeedCoherentAsync();
        await ExecuteAsync($"UPDATE {Approvals} SET \"ProjectId\" = lower(\"ProjectId\"), \"GitWorkspaceId\" = lower(\"GitWorkspaceId\")");

        var reading = await ReadAsync(seeded);

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Coherent, reading.Kind);
        Assert.Equal(original, reading.Approval);
    }

    [Fact]
    public async Task A_corrupt_scalar_reading_writes_nothing_and_repairs_nothing()
    {
        var (seeded, original) = await SeedCoherentAsync();
        await ExecuteAsync($"UPDATE {Approvals} SET \"ProjectId\" = 'not-a-guid'");
        async Task<string> Snapshot()
        {
            await using var connection = new SqliteConnection(_fixture.ConnectionString + ";Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT group_concat(quote(\"Id\") || quote(\"ProjectId\") || quote(\"ApprovedAtUtc\"), '|') FROM {Approvals}";
            return (string)(await command.ExecuteScalarAsync())!;
        }

        var before = await Snapshot();
        var reading = await ReadAsync(seeded);

        Assert.Equal(GitHubBranchPublicationApprovalReadingKind.Incoherent, reading.Kind);
        Assert.Equal(before, await Snapshot());
        Assert.Contains("'not-a-guid'", before, StringComparison.Ordinal);
        Assert.NotNull(original);
    }
}
