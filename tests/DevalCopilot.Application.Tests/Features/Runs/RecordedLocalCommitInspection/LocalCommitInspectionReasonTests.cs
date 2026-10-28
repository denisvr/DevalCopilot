using DevalCopilot.Application.Features.Runs.Policies.RecordedLocalCommit;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.RecordedLocalCommitInspection;

/// <summary>The finite reason allowlist of ADR-0070: each exact fixed code is Known and returned, every other bounded nonblank reason
/// (a prefix lookalike, a differently cased code, a dynamically constructed native detail, arbitrary text, a 128-character reason) is
/// Unrecognized with no code and does not invalidate the otherwise coherent operation, and the raw stored text is never returned.</summary>
public sealed class LocalCommitInspectionReasonTests : LocalCommitInspectionTestBase
{
    private static readonly string[] AllowlistedCodes =
    [
        "local_commit.authority_changed",
        "local_commit.git_unprovable",
        "local_commit.ownership_unprovable",
        "local_commit.head_not_bound",
        "local_commit.prepared_state_unexpected",
        "local_commit.unpromoted_state_unknown",
        "local_commit.branch_tip_unrecognized",
        "local_commit.unknown_index_lock",
        "local_commit.index_state_unrecognized",
        "local_commit.unknown_reference_lock",
        "local_commit.reference_lock_unproven",
        "local_commit.interrupted_before_execution",
        "local_commit.interrupted_unpromoted",
    ];

    public static TheoryData<string> Known => new(AllowlistedCodes);

    public static TheoryData<string> Unrecognized => new()
    {
        "local_commit.seeded_attention",
        "local_commit.git_unprovable.extra",
        "local_commit.git_unprovable ",
        " local_commit.git_unprovable",
        "LOCAL_COMMIT.GIT_UNPROVABLE",
        "local_commit.",
        "local_commit.git_unprovable: fatal: C:\\Users\\someone\\repo\\.git\\index.lock exists",
        "git_unprovable",
        "x",
        new string('r', 128),
    };

    [Fact]
    public void The_allowlist_is_exactly_the_thirteen_codes_of_the_decision_and_is_unique()
    {
        Assert.Equal(13, RecordedLocalCommitReasons.KnownCodes.Count);
        Assert.Equal(AllowlistedCodes.Order(StringComparer.Ordinal), RecordedLocalCommitReasons.KnownCodes.Order(StringComparer.Ordinal));
        Assert.Equal(13, RecordedLocalCommitReasons.KnownCodes.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(Known))]
    public async Task Each_allowlisted_code_is_known_and_returned_for_every_state_that_carries_a_reason(string code)
    {
        foreach (var shape in new[]
                 {
                     LocalCommitInspectionShape.FailedStarted, LocalCommitInspectionShape.InterruptedUnstarted,
                     LocalCommitInspectionShape.AttentionUnstarted,
                 })
        {
            var rows = await SeedAsync(shape, code);

            var operation = Recorded(await InspectAsync(rows.Run.Id));

            Assert.Equal(new RecordedLocalCommitReason(RecordedLocalCommitReasonKind.Known, code), operation.Reason);
        }
    }

    [Theory]
    [MemberData(nameof(Unrecognized))]
    public async Task Every_other_bounded_reason_is_unrecognized_with_no_code_and_keeps_the_operation_recorded(string reason)
    {
        var rows = await SeedAsync(LocalCommitInspectionShape.FailedStarted, reason);

        var operation = Recorded(await InspectAsync(rows.Run.Id));

        Assert.Equal(RecordedLocalCommitReasonKind.Unrecognized, operation.Reason.Kind);
        Assert.Null(operation.Reason.Code);
        Assert.Equal(LocalCommitStatus.Failed, operation.Status);
        Assert.Equal(LocalCommitInspectionSeed.Finished, operation.CompletedAtUtc);
    }
}
