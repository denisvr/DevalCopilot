using DevalCopilot.Application.Features.Runs.Policies.LocalCommit;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The expected worktree tip after host commits (ADR-0029) is the unique coherent chain of completed recorded
/// parent-to-commit edges rooted at the immutable source commit; any gap, fork, cycle or foreign edge has no tip.</summary>
public sealed class LocalCommitHeadChainTests
{
    private static readonly string Source = Oid('0');

    private static string Oid(char value) => new(value, 40);

    private static LocalCommitOperation Edge(string parent, string commit, LocalCommitStatus status = LocalCommitStatus.Completed)
    {
        var id = Guid.NewGuid();
        var operation = LocalCommitOperation.Prepare(
            new LocalCommitOperation.PreparedFacts(
                id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, new string('c', 64), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new string('c', 64), new string('c', 64), "Deliver",
                "branch", parent, new string('e', 40), commit, "Owner", "owner@example.com", 1_800_000_000, new string('c', 64),
                new string('c', 64), "operations/x/prepared.index", 1, 1, LocalCommitRowsSeed.Now),
            [LocalCommitAuthorityMember.Record(
                Guid.NewGuid(), id, LocalCommitAuthorityMemberKind.HumanReview, 0, Guid.NewGuid(), null, new string('c', 64))]);
        if (status != LocalCommitStatus.Prepared)
        {
            operation.MarkExecuting(LocalCommitRowsSeed.Now);
            if (status == LocalCommitStatus.Completed)
            {
                operation.Complete(LocalCommitRowsSeed.Now);
            }
            else if (status == LocalCommitStatus.Failed)
            {
                operation.Fail("local_commit.seeded", LocalCommitRowsSeed.Now);
            }
            else if (status == LocalCommitStatus.NeedsAttention)
            {
                operation.MarkNeedsAttention("local_commit.seeded");
            }
        }

        return operation;
    }

    [Fact]
    public void With_no_completed_operation_the_tip_is_the_immutable_source_commit()
    {
        Assert.Equal(Source, LocalCommitHeadChain.ResolveTip(Source, []));
    }

    [Fact]
    public void A_single_completed_edge_extends_the_tip()
    {
        Assert.Equal(Oid('1'), LocalCommitHeadChain.ResolveTip(Source, [Edge(Source, Oid('1'))]));
    }

    [Fact]
    public void A_chain_resolves_regardless_of_the_order_the_edges_arrive_in()
    {
        var edges = new[] { Edge(Oid('2'), Oid('3')), Edge(Source, Oid('1')), Edge(Oid('1'), Oid('2')) };

        Assert.Equal(Oid('3'), LocalCommitHeadChain.ResolveTip(Source, edges));
        Assert.Equal(Oid('3'), LocalCommitHeadChain.ResolveTip(Source, edges.Reverse().ToArray()));
    }

    [Theory]
    [InlineData(LocalCommitStatus.Prepared)]
    [InlineData(LocalCommitStatus.Executing)]
    [InlineData(LocalCommitStatus.Failed)]
    [InlineData(LocalCommitStatus.NeedsAttention)]
    public void Only_completed_operations_are_edges(LocalCommitStatus status)
    {
        var tip = LocalCommitHeadChain.ResolveTip(Source, [Edge(Source, Oid('1')), Edge(Oid('1'), Oid('2'), status)]);

        Assert.Equal(Oid('1'), tip);
    }

    [Fact]
    public void A_gap_has_no_tip_because_the_chain_does_not_use_every_completed_edge()
    {
        Assert.Null(LocalCommitHeadChain.ResolveTip(Source, [Edge(Source, Oid('1')), Edge(Oid('8'), Oid('9'))]));
    }

    [Fact]
    public void A_fork_has_no_tip()
    {
        Assert.Null(LocalCommitHeadChain.ResolveTip(Source, [Edge(Source, Oid('1')), Edge(Source, Oid('2'))]));
        Assert.Null(LocalCommitHeadChain.ResolveTip(
            Source, [Edge(Source, Oid('1')), Edge(Oid('1'), Oid('2')), Edge(Oid('1'), Oid('3'))]));
    }

    [Fact]
    public void A_cycle_or_a_self_edge_has_no_tip()
    {
        Assert.Null(LocalCommitHeadChain.ResolveTip(Source, [Edge(Source, Oid('1')), Edge(Oid('1'), Source)]));
        Assert.Null(LocalCommitHeadChain.ResolveTip(Source, [Edge(Oid('1'), Oid('1'))]));
        Assert.Null(LocalCommitHeadChain.ResolveTip(Source, [Edge(Source, Oid('1')), Edge(Oid('1'), Oid('2')), Edge(Oid('2'), Oid('1'))]));
    }

    [Fact]
    public void Two_operations_producing_the_same_commit_are_ambiguous()
    {
        Assert.Null(LocalCommitHeadChain.ResolveTip(Source, [Edge(Source, Oid('1')), Edge(Oid('7'), Oid('1'))]));
    }

    [Fact]
    public void The_tip_is_derived_from_edges_never_from_the_arrival_order_or_a_timestamp()
    {
        var first = Edge(Source, Oid('1'));
        var second = Edge(Oid('1'), Oid('2'));

        Assert.Equal(Oid('2'), LocalCommitHeadChain.ResolveTip(Source, [second, first]));
        Assert.Equal(Oid('2'), LocalCommitHeadChain.ResolveTip(Source, [first, second]));
    }
}
