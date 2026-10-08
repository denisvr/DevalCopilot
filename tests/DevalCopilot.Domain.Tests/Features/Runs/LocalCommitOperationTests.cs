using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

/// <summary>The ADR-0029 operation aggregate: every identifying fact is validated and immutable once prepared, and the only
/// mutable thing, its status, moves through guarded transitions that never turn ambiguity into success or failure.</summary>
public sealed class LocalCommitOperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Sha256 = new('a', 64);
    private static readonly string Oid = new('b', 40);

    private static LocalCommitOperation.PreparedFacts Facts(Guid? id = null, Func<LocalCommitOperation.PreparedFacts, LocalCommitOperation.PreparedFacts>? change = null)
    {
        var facts = new LocalCommitOperation.PreparedFacts(
            id ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, Sha256,
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Sha256, Sha256, "Deliver the change",
            "devalcopilot/workspace/p/1", Oid, new string('c', 40), new string('d', 40), "Local Owner", "owner@example.com",
            1_800_000_000, Sha256, Sha256, @"operations\abc\prepared.index", 2, 100, Now);
        return change is null ? facts : change(facts);
    }

    private static LocalCommitAuthorityMember Member(Guid operationId, int sequence = 0, LocalCommitAuthorityMemberKind kind = LocalCommitAuthorityMemberKind.Verification) =>
        LocalCommitAuthorityMember.Record(Guid.NewGuid(), operationId, kind, sequence, Guid.NewGuid(), kind == LocalCommitAuthorityMemberKind.Verification ? Guid.NewGuid() : null, Sha256);

    private static LocalCommitOperation Prepared(out Guid id)
    {
        id = Guid.NewGuid();
        return LocalCommitOperation.Prepare(Facts(id), [Member(id)]);
    }

    private static LocalCommitOperation Executing()
    {
        var operation = Prepared(out _);
        operation.MarkExecuting(Now);
        return operation;
    }

    private const string Identity = "00000000deadbeef:00112233445566778899aabbccddeeff";

    [Fact]
    public void Prepare_records_the_immutable_facts_in_the_prepared_state()
    {
        var operation = Prepared(out var id);

        Assert.Equal(LocalCommitStatus.Prepared, operation.Status);
        Assert.Equal(id, operation.Id);
        Assert.Null(operation.ExecutionStartedAtUtc);
        Assert.False(operation.IsTerminal);
        Assert.Equal("Deliver the change", operation.NormalizedMessage);
        Assert.Equal($"Deliver the change\n\nDevalCopilot-Operation: {id:D}\n", operation.CommitMessage);
        Assert.Single(operation.Members);
    }

    public static TheoryData<string, Func<LocalCommitOperation.PreparedFacts, LocalCommitOperation.PreparedFacts>> InvalidFacts => new()
    {
        { "empty run", facts => facts with { RunId = Guid.Empty } },
        { "empty lease", facts => facts with { RepositoryMutationLeaseId = Guid.Empty } },
        { "empty human review", facts => facts with { HumanCheckpointReviewId = Guid.Empty } },
        { "checkpoint number", facts => facts with { CheckpointNumber = 0 } },
        { "no changed paths", facts => facts with { ChangedPathCount = 0 } },
        { "too many paths", facts => facts with { ChangedPathCount = LocalCommitOperation.MaximumChangedPaths + 1 } },
        { "too many bytes", facts => facts with { TotalBytes = LocalCommitOperation.MaximumTotalBytes + 1 } },
        { "negative bytes", facts => facts with { TotalBytes = -1 } },
        { "short fingerprint", facts => facts with { CheckpointFingerprintSha256 = "abc" } },
        { "uppercase digest", facts => facts with { AuthoritySha256 = new string('A', 64) } },
        { "short parent", facts => facts with { ParentCommitSha = new string('b', 39) } },
        { "non-hex tree", facts => facts with { TreeSha = new string('z', 40) } },
        { "unnormalized message", facts => facts with { NormalizedMessage = "  padded  " } },
        { "forged trailer", facts => facts with { NormalizedMessage = "Subject\nDevalCopilot-Operation: forged" } },
        { "blank branch", facts => facts with { BranchName = " " } },
        { "blank author", facts => facts with { AuthorName = string.Empty } },
        { "blank artifact", facts => facts with { PreparedIndexRelativePath = string.Empty } },
    };

    [Theory]
    [MemberData(nameof(InvalidFacts))]
    public void Prepare_refuses_incomplete_or_unbounded_facts(
        string label, Func<LocalCommitOperation.PreparedFacts, LocalCommitOperation.PreparedFacts> change)
    {
        var id = Guid.NewGuid();
        var exception = Record.Exception(() => LocalCommitOperation.Prepare(Facts(id, change), [Member(id)]));

        Assert.True(exception is ArgumentException, label);
    }

    [Fact]
    public void Prepare_requires_coherent_unique_authority_membership()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => LocalCommitOperation.Prepare(Facts(id), []));
        Assert.Throws<ArgumentException>(() => LocalCommitOperation.Prepare(Facts(id), [Member(Guid.NewGuid())]));
        Assert.Throws<ArgumentException>(() => LocalCommitOperation.Prepare(Facts(id), [Member(id, 0), Member(id, 0)]));
        var mixed = LocalCommitOperation.Prepare(
            Facts(id), [Member(id, 0), Member(id, 0, LocalCommitAuthorityMemberKind.HumanReview)]);
        Assert.Equal(2, mixed.Members.Count);
    }

    [Fact]
    public void Authority_members_require_identity_a_command_for_verification_and_a_digest()
    {
        var id = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => LocalCommitAuthorityMember.Record(
            Guid.Empty, id, LocalCommitAuthorityMemberKind.HumanReview, 0, Guid.NewGuid(), null, Sha256));
        Assert.Throws<ArgumentException>(() => LocalCommitAuthorityMember.Record(
            Guid.NewGuid(), id, LocalCommitAuthorityMemberKind.Verification, 0, Guid.NewGuid(), null, Sha256));
        Assert.Throws<ArgumentException>(() => LocalCommitAuthorityMember.Record(
            Guid.NewGuid(), id, LocalCommitAuthorityMemberKind.HumanReview, 0, Guid.NewGuid(), null, "not-a-digest"));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalCommitAuthorityMember.Record(
            Guid.NewGuid(), id, LocalCommitAuthorityMemberKind.HumanReview, -1, Guid.NewGuid(), null, Sha256));
        Assert.Throws<ArgumentOutOfRangeException>(() => LocalCommitAuthorityMember.Record(
            Guid.NewGuid(), id, (LocalCommitAuthorityMemberKind)99, 0, Guid.NewGuid(), null, Sha256));
    }

    [Fact]
    public void The_execution_marker_is_single_use()
    {
        var operation = Prepared(out _);

        operation.MarkExecuting(Now);

        Assert.Equal(LocalCommitStatus.Executing, operation.Status);
        Assert.Equal(Now, operation.ExecutionStartedAtUtc);
        Assert.Throws<InvalidOperationException>(() => operation.MarkExecuting(Now.AddMinutes(1)));
    }

    [Fact]
    public void Completion_is_possible_only_after_the_marker_and_clears_the_reason()
    {
        var notStarted = Prepared(out _);
        Assert.Throws<InvalidOperationException>(() => notStarted.Complete(Now));

        // Attention can be flagged on an operation that never started; it still cannot be turned into a completed delivery.
        var attentionBeforeStart = Prepared(out _);
        attentionBeforeStart.MarkNeedsAttention("local_commit.ownership_unprovable");
        Assert.Throws<InvalidOperationException>(() => attentionBeforeStart.Complete(Now));
        Assert.Equal(LocalCommitStatus.NeedsAttention, attentionBeforeStart.Status);

        var operation = Executing();
        operation.MarkNeedsAttention("local_commit.unknown_index_lock");
        operation.Complete(Now.AddMinutes(1));

        Assert.Equal(LocalCommitStatus.Completed, operation.Status);
        Assert.Null(operation.OutcomeReasonCode);
        Assert.Equal(Now.AddMinutes(1), operation.CompletedAtUtc);
        Assert.True(operation.IsTerminal);
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("interrupt")]
    [InlineData("complete")]
    public void A_terminal_operation_is_immutable(string terminal)
    {
        var operation = Executing();
        switch (terminal)
        {
            case "fail":
                operation.Fail("local_commit.failed", Now);
                break;
            case "interrupt":
                operation.Interrupt("local_commit.interrupted_unpromoted", Now);
                break;
            default:
                operation.Complete(Now);
                break;
        }

        var status = operation.Status;
        Assert.Throws<InvalidOperationException>(() => operation.Fail("again", Now));
        Assert.Throws<InvalidOperationException>(() => operation.Interrupt("again", Now));
        Assert.Throws<InvalidOperationException>(() => operation.MarkNeedsAttention("again"));
        Assert.Throws<InvalidOperationException>(() => operation.Complete(Now));
        Assert.Throws<InvalidOperationException>(() => operation.MarkExecuting(Now));
        Assert.Equal(status, operation.Status);
    }

    [Fact]
    public void Failure_interruption_and_attention_require_a_reason_and_attention_is_never_terminal()
    {
        var operation = Executing();

        Assert.Throws<ArgumentException>(() => operation.Fail(" ", Now));
        Assert.Throws<ArgumentException>(() => operation.Interrupt(string.Empty, Now));
        Assert.Throws<ArgumentException>(() => operation.MarkNeedsAttention(" "));

        operation.MarkNeedsAttention("local_commit.ambiguous");
        Assert.Equal(LocalCommitStatus.NeedsAttention, operation.Status);
        Assert.False(operation.IsTerminal);
        Assert.Null(operation.CompletedAtUtc);

        operation.MarkNeedsAttention("local_commit.another_reason");
        Assert.Equal("local_commit.another_reason", operation.OutcomeReasonCode);
    }

    [Fact]
    public void The_physical_acquisition_receipt_is_recorded_once_only_while_executing()
    {
        var prepared = Prepared(out _);
        Assert.Throws<InvalidOperationException>(() => RecordAcquisition(prepared));

        var operation = Executing();
        RecordAcquisition(operation);

        Assert.Equal(Identity, operation.IndexLockIdentity);
        Assert.NotNull(operation.IndexAcquiredAtUtc);
        Assert.Throws<InvalidOperationException>(() => RecordAcquisition(operation));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("00000000DEADBEEF:00112233445566778899aabbccddeeff")]
    [InlineData("00000000deadbeef-00112233445566778899aabbccddeeff")]
    public void A_receipt_requires_the_exact_volume_and_128_bit_file_id_form(string identity)
    {
        var operation = Executing();

        Assert.Throws<InvalidOperationException>(() => operation.RecordIndexAcquisition(
            identity, Identity, 10, Identity, 10, Identity, 10, Now));
        Assert.Throws<InvalidOperationException>(() => operation.RecordIndexAcquisition(
            Identity, Identity, 10, Identity, 10, identity, 10, Now));
        Assert.Null(operation.IndexAcquiredAtUtc);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, 16L * 1024 * 1024 + 1)]
    public void A_receipt_bounds_every_index_length_including_the_preimage(long preimage, long artifact, long lockLength)
    {
        var operation = Executing();

        Assert.Throws<InvalidOperationException>(() => operation.RecordIndexAcquisition(
            Identity, Identity, preimage, Identity, artifact, Identity, lockLength, Now));
    }

    [Fact]
    public void The_quarantine_plan_needs_a_receipt_a_plain_name_and_is_recorded_once()
    {
        var withoutReceipt = Executing();
        Assert.Throws<InvalidOperationException>(() => withoutReceipt.PlanIndexReplacement("quarantine", Now));

        var operation = Executing();
        RecordAcquisition(operation);
        Assert.Throws<InvalidOperationException>(() => operation.PlanIndexReplacement(@"nested\name", Now));
        Assert.Throws<InvalidOperationException>(() => operation.PlanIndexReplacement(" ", Now));
        Assert.Throws<InvalidOperationException>(() => operation.PlanIndexReplacement(new string('x', 241), Now));

        operation.PlanIndexReplacement("devalcopilot-quarantine.index-preimage", Now);
        Assert.Equal("devalcopilot-quarantine.index-preimage", operation.IndexQuarantineName);
        Assert.Throws<InvalidOperationException>(() => operation.PlanIndexReplacement("again", Now));
    }

    [Fact]
    public void A_recovery_renewal_replaces_the_receipt_only_after_the_plan_is_durable_and_never_revives_old_ownership()
    {
        var operation = Executing();
        RecordAcquisition(operation);
        var renewedIdentity = "00000000deadbeef:ffeeddccbbaa99887766554433221100";
        Assert.Throws<InvalidOperationException>(() => operation.RenewIndexAcquisitionForRecovery(
            Identity, Identity, 10, Identity, 10, renewedIdentity, 10, Now));

        operation.PlanIndexReplacement("devalcopilot-quarantine.index-preimage", Now);
        operation.RenewIndexAcquisitionForRecovery(Identity, Identity, 10, Identity, 10, renewedIdentity, 10, Now.AddMinutes(1));

        Assert.Equal(renewedIdentity, operation.IndexLockIdentity);
        Assert.Equal(Now.AddMinutes(1), operation.IndexAcquiredAtUtc);

        var attention = Executing();
        RecordAcquisition(attention);
        attention.PlanIndexReplacement("devalcopilot-quarantine.index-preimage", Now);
        attention.MarkNeedsAttention("local_commit.unknown_index_lock");
        Assert.Throws<InvalidOperationException>(() => attention.RenewIndexAcquisitionForRecovery(
            Identity, Identity, 10, Identity, 10, renewedIdentity, 10, Now));
    }

    private static void RecordAcquisition(LocalCommitOperation operation) =>
        operation.RecordIndexAcquisition(Identity, Identity, 100, Identity, 100, Identity, 100, Now);
}
