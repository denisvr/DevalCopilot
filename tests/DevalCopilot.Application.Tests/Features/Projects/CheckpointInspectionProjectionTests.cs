using DevalCopilot.Application.Features.Projects.Policies;
using DevalCopilot.Application.Features.Projects.Ports;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedFixture;

namespace DevalCopilot.Application.Tests.Features.Projects;

/// <summary>The inspection projection removes exactly what must not accompany a human comparison (the raw patch, untracked previews and
/// instruction context) and keeps the identity and the attested facts; the Agent projection is a different, unchanged policy.</summary>
public sealed class CheckpointInspectionProjectionTests
{
    private static GitWorkspaceEvidenceResult Full() => new(
        GitWorkspaceEvidenceOutcome.Success,
        new string('a', 40),
        new string('b', 64),
        [Modified("a.txt"), Modified("AGENTS.md")],
        "diff --git a/a.txt b/a.txt\n+RAW\n",
        [],
        new GitWorkspaceInstructionContext([]),
        [Edit("a.txt", "1\n", "2\n"), Edit("AGENTS.md", "1\n", "2\n")]);

    [Fact]
    public void The_raw_patch_previews_and_instruction_context_are_removed_and_identity_and_facts_are_kept()
    {
        var full = Full();

        var projected = CheckpointInspectionProjection.Project(full);

        Assert.Null(projected.CompleteDiff);
        Assert.Null(projected.UntrackedFiles);
        Assert.Null(projected.InstructionContext);
        Assert.Equal(full.FingerprintSha256, projected.FingerprintSha256);
        Assert.Equal(full.HeadCommitSha, projected.HeadCommitSha);
        Assert.Equal(full.ChangedPaths, projected.ChangedPaths);
        Assert.Equal(full.TrackedFiles, projected.TrackedFiles);
        Assert.Equal(CheckpointInspectionProjection.Project(projected), projected);
    }

    [Fact]
    public void The_inspection_projection_does_not_reserve_the_root_names_while_the_agent_projection_keeps_its_own_policy()
    {
        var human = CheckpointInspectionProjection.Project(Full());
        var agent = AgentEvidenceProjection.Project(Full());

        Assert.Equal(["a.txt", "AGENTS.md"], human.TrackedFiles!.Select(file => file.Path));
        Assert.NotNull(agent.InstructionContext);
        Assert.Null(agent.CompleteDiff);
        Assert.Equal([], agent.UntrackedFiles);
    }
}
