using System.Security.Cryptography;
using System.Text;
using DevalCopilot.Application.Features.Runs.Policies.LocalExecutionSequences;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs.LocalExecutionSequences;

/// <summary>The versioned confirmation value of ADR-0060 is pinned: its version line, its framing (UTF-8 byte length, a colon and the
/// text; an absent value is a dash), its member order and its injectivity. A change to any of them is a new version.</summary>
public sealed class LocalExecutionCandidateConfirmationTests
{
    private static readonly Guid Run = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Project = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Plan = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Workspace = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid Checkpoint = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid Acceptance = Guid.Parse("00000000-0000-0000-0000-000000000006");
    private static readonly Guid Recipe = Guid.Parse("00000000-0000-0000-0000-000000000007");
    private static readonly DateTimeOffset Configured = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private static LocalExecutionCandidate Candidate(
        string settings = "settings", int allowance = 2, IReadOnlyList<LocalExecutionRecipePin>? recipes = null) => new(
        Run, Project, Plan, LocalExecutionPlanAuthority.AcceptedRoot, [Plan, Acceptance], null, Workspace, Checkpoint,
        new string('a', 64), settings, allowance, recipes ?? [Pin(0, "Unit", "[\"test\"]")]);

    private static LocalExecutionRecipePin Pin(int ordinal, string name, string arguments, int timeout = 300) => new(
        ordinal, Recipe, ordinal + 1, name, @"C:\tools\dotnet.exe", arguments, timeout, true, Configured, Configured);

    /// <summary>The independent, hand-written canonical text of <see cref="Candidate"/> with its default recipe.</summary>
    private static string ExpectedCanonicalText() =>
        string.Join('\n',
            "devalcopilot.local-execution-sequence.confirmation.v1",
            $"run=36:{Run:D}",
            $"project=36:{Project:D}",
            $"plan=36:{Plan:D}",
            "authority=12:AcceptedRoot",
            "planInputs=64:" + LocalExecutionCandidate.HashPlanInputs([Plan, Acceptance], null),
            $"workspace=36:{Workspace:D}",
            $"checkpoint=36:{Checkpoint:D}",
            "fingerprint=64:" + new string('a', 64),
            "settings=8:settings",
            "allowance=1:2",
            "recipeCount=1:1",
            "recipe.ordinal=1:0",
            $"recipe.id=36:{Recipe:D}",
            "recipe.number=1:1",
            "recipe.name=4:Unit",
            @"recipe.path=19:C:\tools\dotnet.exe",
            "recipe.arguments=8:[\"test\"]",
            "recipe.timeout=3:300",
            "recipe.enabled=1:1",
            $"recipe.configuredTicks={Configured.UtcTicks.ToString().Length}:{Configured.UtcTicks}",
            $"recipe.updatedTicks={Configured.UtcTicks.ToString().Length}:{Configured.UtcTicks}",
            string.Empty);

    [Fact]
    public void The_confirmation_is_the_sha256_of_the_pinned_canonical_framed_encoding()
    {
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ExpectedCanonicalText()))).ToLowerInvariant();

        Assert.Equal(expected, Candidate().ConfirmationSha256);
    }

    [Fact]
    public void The_confirmation_matches_its_golden_value()
    {
        Assert.Equal(GoldenConfirmation, Candidate().ConfirmationSha256);
        Assert.Equal(GoldenPlanInputs, Candidate().PlanInputsSha256);
    }

    // The values below are pinned from version "...confirmation.v1" / "...plan-inputs.v1". A different value means the encoding changed:
    // introduce a new version line instead of editing these.
    private const string GoldenConfirmation = "8d5375f6f35e0176a000b4a8af9643050d73259cd51c355e847d8937162e5b01";
    private const string GoldenPlanInputs = "822102eb00cee1ebf7ebe01fd8f455063048976322e3dbaa4a7fb5ce19f83036";

    [Fact]
    public void Every_bound_member_changes_the_confirmation()
    {
        var baseline = Candidate().ConfirmationSha256;
        var variants = new[]
        {
            Candidate(settings: "settings2").ConfirmationSha256,
            Candidate(allowance: 1).ConfirmationSha256,
            (Candidate() with { RunId = Guid.NewGuid() }).ConfirmationSha256,
            (Candidate() with { ProjectId = Guid.NewGuid() }).ConfirmationSha256,
            (Candidate() with { PlanProposalMessageId = Guid.NewGuid() }).ConfirmationSha256,
            (Candidate() with { PlanAuthority = LocalExecutionPlanAuthority.AcceptedRevision }).ConfirmationSha256,
            (Candidate() with { PlanInputMessageIds = [Plan] }).ConfirmationSha256,
            (Candidate() with { PlanGrantId = Guid.NewGuid() }).ConfirmationSha256,
            (Candidate() with { WorkspaceId = Guid.NewGuid() }).ConfirmationSha256,
            (Candidate() with { CheckpointId = Guid.NewGuid() }).ConfirmationSha256,
            (Candidate() with { FingerprintSha256 = new string('b', 64) }).ConfirmationSha256,
            Candidate(recipes: [Pin(0, "Other", "[\"test\"]")]).ConfirmationSha256,
            Candidate(recipes: [Pin(0, "Unit", "[\"test\",\"x\"]")]).ConfirmationSha256,
            Candidate(recipes: [Pin(0, "Unit", "[\"test\"]", timeout: 301)]).ConfirmationSha256,
            Candidate(recipes: [Pin(0, "Unit", "[\"test\"]"), Pin(1, "Unit", "[\"test\"]")]).ConfirmationSha256,
            Candidate(recipes: [Pin(0, "Unit", "[\"test\"]") with { IsEnabled = false }]).ConfirmationSha256,
            Candidate(recipes: [Pin(0, "Unit", "[\"test\"]") with { UpdatedAtUtc = Configured.AddTicks(1) }]).ConfirmationSha256,
        };

        Assert.DoesNotContain(baseline, variants);
        Assert.Equal(variants.Length, variants.Distinct().Count());
    }

    [Fact]
    public void The_length_framing_keeps_adjacent_text_members_unambiguous()
    {
        var first = Candidate(recipes: [Pin(0, "ab", "[\"c\"]")]).ConfirmationSha256;
        var second = Candidate(recipes: [Pin(0, "a", "b[\"c\"]")]).ConfirmationSha256;
        var withNewline = Candidate(recipes: [Pin(0, "ab\nrecipe.path=1:x", "[\"c\"]")]).ConfirmationSha256;

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, withNewline);
    }

    [Fact]
    public void The_recipe_order_is_part_of_the_confirmation()
    {
        var one = Pin(0, "One", "[]");
        var two = Pin(1, "Two", "[]") with { CommandId = Guid.NewGuid() };

        var inOrder = Candidate(recipes: [one, two]).ConfirmationSha256;
        var swapped = Candidate(recipes: [two with { Ordinal = 0 }, one with { Ordinal = 1 }]).ConfirmationSha256;

        Assert.NotEqual(inOrder, swapped);
    }

    [Fact]
    public void The_plan_input_identity_is_ordered_and_framed_by_count()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Assert.NotEqual(LocalExecutionCandidate.HashPlanInputs([a, b], null), LocalExecutionCandidate.HashPlanInputs([b, a], null));
        Assert.NotEqual(LocalExecutionCandidate.HashPlanInputs([a], b), LocalExecutionCandidate.HashPlanInputs([a, b], null));
        Assert.NotEqual(LocalExecutionCandidate.HashPlanInputs([a], null), LocalExecutionCandidate.HashPlanInputs([a], b));
    }
}
