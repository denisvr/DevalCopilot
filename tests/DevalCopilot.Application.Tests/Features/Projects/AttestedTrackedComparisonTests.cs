using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedFixture;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>The one source admission and host comparison shared by Agent delivery and human checkpoint inspection (ADR-0024,
/// ADR-0027). The two purposes differ ONLY in the treatment of the two root instruction names; every other proof and bound is
/// common, nothing is taken from a raw patch, and every tracked changed path is accounted for exactly once.</summary>
public sealed class AttestedTrackedComparisonTests
{
    private static string[] Outcome(IReadOnlyList<AttestedTrackedComparison.Entry> entries) =>
        [.. entries.Select(entry => $"{entry.Path}:{entry.Reason ?? "compared"}")];

    [Theory]
    [InlineData("AGENTS.md")]
    [InlineData("CLAUDE.md")]
    [InlineData("agents.md")]
    public void A_proven_root_instruction_file_is_compared_for_humans_and_reserved_for_agents(string name)
    {
        IReadOnlyList<GitWorkspaceTrackedFile> facts = [Edit(name, "old\n", "new\n")];
        GitWorkspaceChangedPath[] paths = [Modified(name)];

        var human = AttestedTrackedComparison.Derive(paths, facts, TrackedSourcePurpose.HumanInspection);
        var agent = AttestedTrackedComparison.Derive(paths, facts, TrackedSourcePurpose.AgentDelivery);
        var delivered = TrackedChangeEvidence.Derive(paths, facts);

        Assert.Equal([$"{name}:compared"], Outcome(human));
        Assert.Contains("+new\n", human[0].Block, StringComparison.Ordinal);
        Assert.Equal([$"{name}:reserved_instruction_file"], Outcome(agent));
        Assert.Null(agent[0].Block);
        Assert.Equal(string.Empty, delivered.Text);
        Assert.Equal([new TrackedChangeEvidence.Omission(name, "reserved_instruction_file")], delivered.Omissions);
    }

    [Fact]
    public void A_nested_file_with_an_instruction_name_is_an_ordinary_file_for_both_purposes()
    {
        IReadOnlyList<GitWorkspaceTrackedFile> facts = [Edit("docs/AGENTS.md", "a\n", "b\n")];
        GitWorkspaceChangedPath[] paths = [Modified("docs/AGENTS.md")];

        Assert.Equal(["docs/AGENTS.md:compared"], Outcome(AttestedTrackedComparison.Derive(paths, facts, TrackedSourcePurpose.HumanInspection)));
        Assert.Equal(["docs/AGENTS.md:compared"], Outcome(AttestedTrackedComparison.Derive(paths, facts, TrackedSourcePurpose.AgentDelivery)));
    }

    [Theory]
    [InlineData(TrackedSourcePurpose.AgentDelivery)]
    [InlineData(TrackedSourcePurpose.HumanInspection)]
    public void Both_purposes_share_every_proof_and_account_every_tracked_path_exactly_once_in_ordinal_order(TrackedSourcePurpose purpose)
    {
        GitWorkspaceChangedPath[] paths =
        [
            Modified("z-dup.txt"), Modified("z-dup.txt"), Modified("m-missing.txt"), Untracked("u.txt"), Modified("c-extra-claim.txt"),
            new("t-typechange.txt", null, " ", "T"), Modified("b-both-absent.txt"), Modified("a-ok.txt"), Modified("y-omitted.txt"),
            Modified("x-text-and-omission.txt"), Modified("d-binaryish.txt"),
        ];
        GitWorkspaceTrackedFile[] facts =
        [
            Edit("a-ok.txt", "1\n", "2\n"),
            Edit("z-dup.txt", "1\n", "2\n"),
            Edit("t-typechange.txt", "1\n", "2\n"),
            new("b-both-absent.txt", null, null, null),
            Omit("y-omitted.txt", GitWorkspaceTrackedOmission.ContainmentUnproven),
            new("x-text-and-omission.txt", GitWorkspaceTrackedOmission.Binary, "a\n", "b\n"),
            Edit("d-binaryish.txt", "a\n", "b\0c\n"),
            Edit("u.txt", "claimed\n", "for an untracked path\n"),
            Edit("not-in-the-capture.txt", "claimed\n", "for no path\n"),
        ];

        var entries = AttestedTrackedComparison.Derive(paths, facts, purpose);

        Assert.Equal(
            [
                "a-ok.txt:compared",
                "b-both-absent.txt:attestation_incoherent",
                "c-extra-claim.txt:not_attested",
                "d-binaryish.txt:attestation_incoherent",
                "m-missing.txt:not_attested",
                "t-typechange.txt:unsupported_status",
                "x-text-and-omission.txt:attestation_incoherent",
                "y-omitted.txt:containment_unproven",
                "z-dup.txt:attestation_incoherent",
            ],
            Outcome(entries));
        Assert.DoesNotContain("claimed", string.Concat(entries.Select(entry => entry.Block)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TrackedSourcePurpose.AgentDelivery)]
    [InlineData(TrackedSourcePurpose.HumanInspection)]
    public void A_capture_with_no_attestation_compares_nothing_and_is_never_completed_from_a_patch(TrackedSourcePurpose purpose)
    {
        var entries = AttestedTrackedComparison.Derive([Modified("b.txt"), Untracked("u.txt"), Modified("a.txt")], null, purpose);

        Assert.Equal(["a.txt:not_attested", "b.txt:not_attested"], Outcome(entries));
        Assert.All(entries, entry => Assert.Null(entry.Block));
    }

    [Theory]
    [InlineData(TrackedSourcePurpose.AgentDelivery)]
    [InlineData(TrackedSourcePurpose.HumanInspection)]
    public void The_retained_source_budget_is_common_to_both_purposes(TrackedSourcePurpose purpose)
    {
        var half = new string('x', 200 * 1024 - 1) + "\n";
        GitWorkspaceChangedPath[] paths = [Modified("a.txt"), Modified("b.txt"), Modified("c.txt")];
        GitWorkspaceTrackedFile[] facts =
        [
            Edit("a.txt", half, half.Replace('x', 'y')),
            Edit("b.txt", half, half.Replace('x', 'z')),
            Edit("c.txt", half, half.Replace('x', 'w')),
        ];

        var entries = AttestedTrackedComparison.Derive(paths, facts, purpose);

        Assert.Equal(["a.txt:compared", "b.txt:aggregate_limit", "c.txt:aggregate_limit"], Outcome(entries));
    }

    [Fact]
    public void The_two_purposes_differ_only_in_the_reserved_root_names()
    {
        GitWorkspaceChangedPath[] paths = [Modified("AGENTS.md"), Modified("CLAUDE.md"), Modified("a.txt"), Added("b.txt"), Deleted("c.txt")];
        GitWorkspaceTrackedFile[] facts =
        [
            Edit("AGENTS.md", "1\n", "2\n"), Edit("CLAUDE.md", "1\n", "2\n"), Edit("a.txt", "1\n", "2\n"),
            Add("b.txt", "new\n"), Delete("c.txt", "old\n"),
        ];

        var human = AttestedTrackedComparison.Derive(paths, facts, TrackedSourcePurpose.HumanInspection);
        var agent = AttestedTrackedComparison.Derive(paths, facts, TrackedSourcePurpose.AgentDelivery);

        Assert.Equal(human.Count, agent.Count);
        var differing = human.Zip(agent).Where(pair => pair.First != pair.Second).Select(pair => pair.First.Path).ToArray();
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], differing);
    }
}
