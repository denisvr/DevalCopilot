using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// The complete decision table of ADR-0029 startup recovery. Recovery decides only from the recorded operation and one exact
/// inspection: it completes only a fully proven promotion, interrupts only a fully proven unpromoted state, finishes a pending
/// index only from a durable plan with an absent lock, and everything else is attention. It never decides from an exit code, a
/// reflog or a plausible commit.
/// </summary>
public sealed class LocalCommitRecoveryPolicyTests
{
    private static readonly string Parent = new('a', 40);
    private static readonly string Commit = new('d', 40);
    private static readonly string Foreign = new('f', 40);

    private static LocalCommitOperation Operation(
        LocalCommitStatus status, bool acquired = false, bool planned = false)
    {
        var id = Guid.NewGuid();
        var operation = LocalCommitOperation.Prepare(
            new LocalCommitOperation.PreparedFacts(
                id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('c', 64), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('c', 64), new string('c', 64), "Deliver",
                "branch", Parent, new string('e', 40), Commit, "Owner", "owner@example.com", 1_800_000_000, new string('c', 64),
                new string('c', 64), "operations/x/prepared.index", 1, 1, LocalCommitRowsSeed.Now),
            [LocalCommitAuthorityMember.Record(
                Guid.NewGuid(), id, LocalCommitAuthorityMemberKind.HumanReview, 0, Guid.NewGuid(), null, new string('c', 64))]);
        if (status == LocalCommitStatus.Prepared)
        {
            return operation;
        }

        operation.MarkExecuting(LocalCommitRowsSeed.Now);
        const string identity = "00000000deadbeef:00112233445566778899aabbccddeeff";
        if (acquired)
        {
            operation.RecordIndexAcquisition(identity, identity, 1, identity, 1, identity, 1, LocalCommitRowsSeed.Now);
        }

        if (planned)
        {
            operation.PlanIndexReplacement("quarantine.index-preimage", LocalCommitRowsSeed.Now);
        }

        switch (status)
        {
            case LocalCommitStatus.NeedsAttention:
                operation.MarkNeedsAttention("local_commit.seeded");
                break;
            case LocalCommitStatus.Completed:
                operation.Complete(LocalCommitRowsSeed.Now);
                break;
            case LocalCommitStatus.Failed:
                operation.Fail("local_commit.seeded", LocalCommitRowsSeed.Now);
                break;
            case LocalCommitStatus.Interrupted:
                operation.Interrupt("local_commit.seeded", LocalCommitRowsSeed.Now);
                break;
        }

        return operation;
    }

    private static LocalCommitInspection Inspection(
        string? tip,
        LocalCommitIndexState index,
        LocalCommitLockState indexLock = LocalCommitLockState.None,
        LocalCommitObjectState commit = LocalCommitObjectState.ExactMatch,
        bool owned = true,
        bool bound = true,
        bool artifact = true,
        bool source = true,
        LocalCommitInspectionOutcome outcome = LocalCommitInspectionOutcome.Observed,
        LocalCommitReferenceLockState referenceLocks = LocalCommitReferenceLockState.Clear) =>
        new(outcome, owned, bound, tip, commit, index, indexLock, artifact, source, referenceLocks);

    private static LocalCommitRecoveryDecision Decide(LocalCommitOperation operation, LocalCommitInspection inspection) =>
        LocalCommitRecoveryPolicy.Decide(operation, inspection);

    [Theory]
    [InlineData(LocalCommitStatus.Completed)]
    [InlineData(LocalCommitStatus.Failed)]
    [InlineData(LocalCommitStatus.Interrupted)]
    public void A_terminal_operation_is_never_decided_again(LocalCommitStatus status)
    {
        var decision = Decide(Operation(status), Inspection(Commit, LocalCommitIndexState.Prepared));

        Assert.Equal(LocalCommitRecoveryAction.Attention, decision.Action);
        Assert.Equal("local_commit.already_terminal", decision.ReasonCode);
    }

    [Theory]
    [InlineData(LocalCommitInspectionOutcome.GitUnavailable, "local_commit.git_unprovable")]
    [InlineData(LocalCommitInspectionOutcome.Unprovable, "local_commit.git_unprovable")]
    public void An_unobserved_repository_is_attention(LocalCommitInspectionOutcome outcome, string reason)
    {
        var decision = Decide(Operation(LocalCommitStatus.Executing), Inspection(Parent, LocalCommitIndexState.Preimage, outcome: outcome));

        Assert.Equal(LocalCommitRecoveryAction.Attention, decision.Action);
        Assert.Equal(reason, decision.ReasonCode);
    }

    [Fact]
    public void Unproven_ownership_or_an_unbound_head_is_attention_before_anything_else()
    {
        var operation = Operation(LocalCommitStatus.Executing);

        Assert.Equal(
            "local_commit.ownership_unprovable",
            Decide(operation, Inspection(Parent, LocalCommitIndexState.Preimage, owned: false)).ReasonCode);
        Assert.Equal(
            "local_commit.head_not_bound",
            Decide(operation, Inspection(Parent, LocalCommitIndexState.Preimage, bound: false)).ReasonCode);
        Assert.Equal(
            "local_commit.head_not_bound",
            Decide(operation, Inspection(null, LocalCommitIndexState.Preimage)).ReasonCode);
    }

    [Theory]
    [InlineData(LocalCommitIndexState.Preimage, LocalCommitLockState.None, LocalCommitRecoveryAction.Interrupt, "local_commit.interrupted_before_execution")]
    [InlineData(LocalCommitIndexState.Preimage, LocalCommitLockState.Unknown, LocalCommitRecoveryAction.Attention, "local_commit.prepared_state_unexpected")]
    [InlineData(LocalCommitIndexState.Prepared, LocalCommitLockState.None, LocalCommitRecoveryAction.Attention, "local_commit.prepared_state_unexpected")]
    [InlineData(LocalCommitIndexState.Other, LocalCommitLockState.None, LocalCommitRecoveryAction.Attention, "local_commit.prepared_state_unexpected")]
    [InlineData(LocalCommitIndexState.Missing, LocalCommitLockState.None, LocalCommitRecoveryAction.Attention, "local_commit.prepared_state_unexpected")]
    public void A_prepared_operation_is_interrupted_only_from_the_exact_untouched_state(
        LocalCommitIndexState index, LocalCommitLockState indexLock, LocalCommitRecoveryAction action, string reason)
    {
        var decision = Decide(Operation(LocalCommitStatus.Prepared), Inspection(Parent, index, indexLock));

        Assert.Equal(action, decision.Action);
        Assert.Equal(reason, decision.ReasonCode);
        Assert.False(decision.RemoveOwnedLock);
    }

    [Fact]
    public void A_prepared_operation_whose_branch_moved_is_attention_never_interrupted()
    {
        foreach (var tip in new[] { Commit, Foreign })
        {
            var decision = Decide(Operation(LocalCommitStatus.Prepared), Inspection(tip, LocalCommitIndexState.Preimage));

            Assert.Equal(LocalCommitRecoveryAction.Attention, decision.Action);
            Assert.Equal("local_commit.prepared_state_unexpected", decision.ReasonCode);
        }
    }

    [Theory]
    [InlineData(LocalCommitStatus.Executing)]
    [InlineData(LocalCommitStatus.NeedsAttention)]
    public void An_unpromoted_open_operation_is_interrupted_only_with_preimage_and_no_lock(LocalCommitStatus status)
    {
        var operation = Operation(status, acquired: true);

        var exact = Decide(operation, Inspection(Parent, LocalCommitIndexState.Preimage));
        Assert.Equal(LocalCommitRecoveryAction.Interrupt, exact.Action);
        Assert.Equal("local_commit.interrupted_unpromoted", exact.ReasonCode);

        foreach (var state in new[]
        {
            (LocalCommitIndexState.Preimage, LocalCommitLockState.Unknown),
            (LocalCommitIndexState.Prepared, LocalCommitLockState.None),
            (LocalCommitIndexState.Other, LocalCommitLockState.None),
            (LocalCommitIndexState.Missing, LocalCommitLockState.None),
        })
        {
            var decision = Decide(operation, Inspection(Parent, state.Item1, state.Item2));
            Assert.Equal(LocalCommitRecoveryAction.Attention, decision.Action);
            Assert.Equal("local_commit.unpromoted_state_unknown", decision.ReasonCode);
        }
    }

    [Fact]
    public void A_branch_tip_that_is_neither_the_parent_nor_an_exact_recorded_commit_is_attention()
    {
        var operation = Operation(LocalCommitStatus.Executing, acquired: true);

        Assert.Equal(
            "local_commit.branch_tip_unrecognized",
            Decide(operation, Inspection(Foreign, LocalCommitIndexState.Prepared)).ReasonCode);
        Assert.Equal(
            "local_commit.branch_tip_unrecognized",
            Decide(operation, Inspection(Commit, LocalCommitIndexState.Prepared, commit: LocalCommitObjectState.Mismatch)).ReasonCode);
        Assert.Equal(
            "local_commit.branch_tip_unrecognized",
            Decide(operation, Inspection(Commit, LocalCommitIndexState.Prepared, commit: LocalCommitObjectState.Absent)).ReasonCode);
    }

    [Fact]
    public void Any_unknown_lock_after_the_reference_moved_is_attention_even_with_a_prepared_index()
    {
        var operation = Operation(LocalCommitStatus.Executing, acquired: true, planned: true);

        foreach (var index in Enum.GetValues<LocalCommitIndexState>())
        {
            var decision = Decide(operation, Inspection(Commit, index, LocalCommitLockState.Unknown));
            Assert.Equal(LocalCommitRecoveryAction.Attention, decision.Action);
            Assert.Equal("local_commit.unknown_index_lock", decision.ReasonCode);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_promoted_prepared_index_completes_whether_or_not_the_working_files_still_match(bool sourceConsistent)
    {
        var operation = Operation(LocalCommitStatus.Executing, acquired: true, planned: true);

        var decision = Decide(operation, Inspection(Commit, LocalCommitIndexState.Prepared, source: sourceConsistent));

        Assert.Equal(LocalCommitRecoveryAction.Complete, decision.Action);
        Assert.Equal("local_commit.recovered_promoted", decision.ReasonCode);
    }

    [Fact]
    public void A_pending_index_is_finished_only_from_an_executing_operation_with_a_durable_plan_and_an_intact_artifact()
    {
        var planned = Operation(LocalCommitStatus.Executing, acquired: true, planned: true);

        var finish = Decide(planned, Inspection(Commit, LocalCommitIndexState.Preimage));
        Assert.Equal(LocalCommitRecoveryAction.FinishPromotionThenComplete, finish.Action);
        Assert.Equal("local_commit.recovered_index_finished", finish.ReasonCode);

        Assert.Equal(
            "local_commit.index_state_unrecognized",
            Decide(planned, Inspection(Commit, LocalCommitIndexState.Preimage, artifact: false)).ReasonCode);
        Assert.Equal(
            "local_commit.index_state_unrecognized",
            Decide(Operation(LocalCommitStatus.Executing, acquired: true), Inspection(Commit, LocalCommitIndexState.Preimage)).ReasonCode);
        Assert.Equal(
            "local_commit.index_state_unrecognized",
            Decide(Operation(LocalCommitStatus.Executing), Inspection(Commit, LocalCommitIndexState.Preimage)).ReasonCode);
        // Attention is sticky for a pending index: a status other than Executing is never finished.
        Assert.Equal(
            "local_commit.index_state_unrecognized",
            Decide(Operation(LocalCommitStatus.NeedsAttention, acquired: true, planned: true), Inspection(Commit, LocalCommitIndexState.Preimage)).ReasonCode);
    }

    [Theory]
    [InlineData(LocalCommitIndexState.Other)]
    [InlineData(LocalCommitIndexState.Missing)]
    public void An_unrecognized_or_missing_index_after_the_reference_moved_is_attention(LocalCommitIndexState index)
    {
        var decision = Decide(
            Operation(LocalCommitStatus.Executing, acquired: true, planned: true), Inspection(Commit, index));

        Assert.Equal(LocalCommitRecoveryAction.Attention, decision.Action);
        Assert.Equal("local_commit.index_state_unrecognized", decision.ReasonCode);
    }

    [Fact]
    public void A_default_inspection_shape_never_implies_clear_reference_locks()
    {
        var inspection = new LocalCommitInspection(
            LocalCommitInspectionOutcome.Observed, true, true, Parent, LocalCommitObjectState.ExactMatch, LocalCommitIndexState.Preimage,
            LocalCommitLockState.None, true, true);

        Assert.Equal(LocalCommitReferenceLockState.Unproven, inspection.ReferenceLocks);
        Assert.Equal(LocalCommitReferenceLockState.Unproven, default(LocalCommitReferenceLockState));
        var decision = Decide(Operation(LocalCommitStatus.Executing), inspection);
        Assert.Equal(LocalCommitRecoveryAction.Attention, decision.Action);
        Assert.Equal("local_commit.reference_lock_unproven", decision.ReasonCode);
    }

    public static TheoryData<LocalCommitStatus, string, LocalCommitIndexState, bool, bool> EveryDecisionShape => new()
    {
        // status, tip, index, acquired, planned: each shape decides something other than Attention when the namespace is clear
        { LocalCommitStatus.Prepared, Parent, LocalCommitIndexState.Preimage, false, false },
        { LocalCommitStatus.Executing, Parent, LocalCommitIndexState.Preimage, false, false },
        { LocalCommitStatus.NeedsAttention, Parent, LocalCommitIndexState.Preimage, false, false },
        { LocalCommitStatus.Executing, Commit, LocalCommitIndexState.Prepared, true, true },
        { LocalCommitStatus.NeedsAttention, Commit, LocalCommitIndexState.Prepared, true, true },
        { LocalCommitStatus.Executing, Commit, LocalCommitIndexState.Preimage, true, true },
    };

    [Theory]
    [MemberData(nameof(EveryDecisionShape))]
    public void Every_release_completion_and_recovery_effect_needs_a_positively_clear_reference_namespace(
        LocalCommitStatus status, string tip, LocalCommitIndexState index, bool acquired, bool planned)
    {
        var operation = Operation(status, acquired, planned);
        var clear = Decide(operation, Inspection(tip, index));
        Assert.NotEqual(LocalCommitRecoveryAction.Attention, clear.Action);

        var present = Decide(operation, Inspection(tip, index, referenceLocks: LocalCommitReferenceLockState.Present));
        var unproven = Decide(operation, Inspection(tip, index, referenceLocks: LocalCommitReferenceLockState.Unproven));

        Assert.Equal(LocalCommitRecoveryAction.Attention, present.Action);
        Assert.Equal("local_commit.unknown_reference_lock", present.ReasonCode);
        Assert.False(present.RemoveOwnedLock);
        Assert.Equal(LocalCommitRecoveryAction.Attention, unproven.Action);
        Assert.Equal("local_commit.reference_lock_unproven", unproven.ReasonCode);
        Assert.False(unproven.RemoveOwnedLock);
    }

    [Fact]
    public void Earlier_facts_keep_their_own_reasons_before_the_reference_lock_gate()
    {
        var operation = Operation(LocalCommitStatus.Executing);

        Assert.Equal(
            "local_commit.git_unprovable",
            Decide(operation, Inspection(
                Parent, LocalCommitIndexState.Preimage, outcome: LocalCommitInspectionOutcome.Unprovable,
                referenceLocks: LocalCommitReferenceLockState.Present)).ReasonCode);
        Assert.Equal(
            "local_commit.ownership_unprovable",
            Decide(operation, Inspection(
                Parent, LocalCommitIndexState.Preimage, owned: false, referenceLocks: LocalCommitReferenceLockState.Present)).ReasonCode);
        Assert.Equal(
            "local_commit.head_not_bound",
            Decide(operation, Inspection(
                Parent, LocalCommitIndexState.Preimage, bound: false, referenceLocks: LocalCommitReferenceLockState.Present)).ReasonCode);
    }

    [Fact]
    public void No_decision_ever_asks_for_a_lock_to_be_removed()
    {
        foreach (var status in new[] { LocalCommitStatus.Prepared, LocalCommitStatus.Executing, LocalCommitStatus.NeedsAttention })
        {
            foreach (var tip in new[] { Parent, Commit, Foreign })
            {
                foreach (var index in Enum.GetValues<LocalCommitIndexState>())
                {
                    foreach (var indexLock in Enum.GetValues<LocalCommitLockState>())
                    {
                        var operation = Operation(status, acquired: true, planned: true);
                        Assert.False(Decide(operation, Inspection(tip, index, indexLock)).RemoveOwnedLock);
                    }
                }
            }
        }
    }
}
