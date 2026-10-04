using System.Text;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateVerificationDiagnosisAttempt;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Policies.VerificationDiagnosis;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedFixture;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The seven Agent builders deliver tracked change evidence only through attested facts (ADR-0024): the host comparison inside
/// the existing whole-hunk selection and explicitly incomplete samples, every omission counted by fixed reason at every reduction step,
/// the stated limitation always present, and the 32 KiB ceiling unchanged.</summary>
public sealed class TrackedAttestedManifestTests
{
    private const int Ceiling = ChangeEvidenceManifest.ManifestCeilingBytes;

    private static string Build(
        string variant, IReadOnlyList<GitWorkspaceChangedPath> paths, IReadOnlyList<GitWorkspaceTrackedFile>? facts,
        IReadOnlyList<GitWorkspaceUntrackedFile>? untracked = null, int padding = 0) =>
        UntrackedFileManifestTests.AttestedBuilder(variant)(paths, TrackedChangeEvidence.Derive(paths, facts), untracked, padding);

    private static JsonElement Evidence(string manifest) =>
        JsonDocument.Parse(manifest).RootElement.GetProperty("changeEvidence").Clone();

    private static string Lines(string prefix, int count, int width = 40) =>
        string.Concat(Enumerable.Range(0, count).Select(index => $"{prefix}-{index:D4}-{new string('.', width)}\n"));

    public static IEnumerable<object[]> Variants => UntrackedFileManifestTests.Variants;

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_complete_attested_comparison_is_inlined_exactly_with_the_stated_limitation(string variant)
    {
        var evidence = Evidence(Build(variant, [Modified("src/a.cs")], [Edit("src/a.cs", "x\ny\n", "x\nz\n")]));

        Assert.Equal(
            "diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1,2 +1,2 @@\n x\n-y\n+z\n",
            evidence.GetProperty("diff").GetString());
        Assert.False(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.False(evidence.TryGetProperty("diffSelection", out _));
        var comparison = evidence.GetProperty("trackedComparison");
        Assert.Equal("host_prefix_suffix_v1", comparison.GetProperty("method").GetString());
        var notice = comparison.GetProperty("notice").GetString()!;
        Assert.Contains("not Git's minimal or filter-normalized patch", notice, StringComparison.Ordinal);
        Assert.Contains("line-ending-only differences remain visible", notice, StringComparison.Ordinal);
        Assert.Contains("unchanged lines inside the replaced middle can appear as removed and added", notice, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Missing_attestation_delivers_no_tracked_text_and_counts_every_tracked_path(string variant)
    {
        var manifest = Build(variant, [Modified("a.cs"), Modified("b.cs"), Untracked("u.txt")], null);

        var evidence = Evidence(manifest);
        Assert.Equal(string.Empty, evidence.GetProperty("diff").GetString());
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        var selection = evidence.GetProperty("diffSelection");
        Assert.False(selection.GetProperty("complete").GetBoolean());
        Assert.Equal(2, selection.GetProperty("omissionReasons").GetProperty("not_attested").GetInt32());
        Assert.Equal(2, selection.GetProperty("files").GetProperty("omitted").GetInt32());
        Assert.Equal(
            ["a.cs:not_attested", "b.cs:not_attested"],
            selection.GetProperty("items").EnumerateArray().Select(item => $"{item.GetProperty("path").GetString()}:{item.GetProperty("reason").GetString()}"));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Omissions_sit_beside_delivered_siblings_with_fixed_reasons_and_truthful_counts(string variant)
    {
        IReadOnlyList<GitWorkspaceChangedPath> paths = [Modified("a.bin"), Modified("b.txt"), Deleted("c.txt"), Modified("d.txt"), Modified("AGENTS.md")];
        var facts = new[]
        {
            Omit("a.bin", GitWorkspaceTrackedOmission.Binary), Edit("b.txt", "1\n", "2\n"), Delete("c.txt", "bye\n"),
            Omit("d.txt", GitWorkspaceTrackedOmission.ContainmentUnproven), Edit("AGENTS.md", "1\n", "RESERVED-TEXT\n"),
        };

        var manifest = Build(variant, paths, facts);

        Assert.DoesNotContain("RESERVED-TEXT", manifest, StringComparison.Ordinal);
        var evidence = Evidence(manifest);
        Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
        var diff = evidence.GetProperty("diff").GetString()!;
        Assert.Contains("+2\n", diff, StringComparison.Ordinal);
        Assert.Contains("-bye\n", diff, StringComparison.Ordinal);
        var selection = evidence.GetProperty("diffSelection");
        Assert.False(selection.GetProperty("complete").GetBoolean());
        Assert.Equal(5, selection.GetProperty("files").GetProperty("total").GetInt32());
        Assert.Equal(2, selection.GetProperty("files").GetProperty("included").GetInt32());
        Assert.Equal(3, selection.GetProperty("files").GetProperty("omitted").GetInt32());
        var reasons = selection.GetProperty("omissionReasons");
        Assert.Equal(1, reasons.GetProperty("binary").GetInt32());
        Assert.Equal(1, reasons.GetProperty("containment_unproven").GetInt32());
        Assert.Equal(1, reasons.GetProperty("reserved_instruction_file").GetInt32());
        Assert.Equal(
            ["AGENTS.md:reserved_instruction_file", "a.bin:binary", "d.txt:containment_unproven"],
            selection.GetProperty("items").EnumerateArray().Select(item => $"{item.GetProperty("path").GetString()}:{item.GetProperty("reason").GetString()}"));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void The_accounting_and_the_limitation_survive_every_reduction_step_and_the_ceiling_fit(string variant)
    {
        IReadOnlyList<GitWorkspaceChangedPath> paths =
        [
            Modified("a.bin"), Modified("big.cs"), Modified("small.cs"), Modified("z-unproven.cs"), Untracked("notes/new.txt"),
        ];
        var facts = new[]
        {
            Omit("a.bin", GitWorkspaceTrackedOmission.Binary),
            Edit("big.cs", Lines("old", 300), Lines("new", 300)),
            Edit("small.cs", "a\n", "b\n"),
            Omit("z-unproven.cs", GitWorkspaceTrackedOmission.ContainmentUnproven),
        };
        var untracked = new[] { new GitWorkspaceUntrackedFile("notes/new.txt", null, 20, "NOTES-KEPT", true) };

        var sawItemsOmitted = false;
        for (var padding = 0; padding <= 31_000; padding += 1_250)
        {
            var manifest = Build(variant, paths, facts, untracked, padding);

            var evidence = Evidence(manifest);
            var selection = evidence.GetProperty("diffSelection");
            Assert.False(selection.GetProperty("complete").GetBoolean());
            Assert.Equal(4, selection.GetProperty("files").GetProperty("total").GetInt32());
            Assert.True(selection.GetProperty("files").GetProperty("omitted").GetInt32() >= 3);
            Assert.Equal(1, selection.GetProperty("omissionReasons").GetProperty("binary").GetInt32());
            Assert.Equal(1, selection.GetProperty("omissionReasons").GetProperty("containment_unproven").GetInt32());
            Assert.True(evidence.GetProperty("diffTruncated").GetBoolean());
            Assert.Equal("host_prefix_suffix_v1", evidence.GetProperty("trackedComparison").GetProperty("method").GetString());
            Assert.Equal(5, evidence.GetProperty("changedPaths").GetArrayLength());
            sawItemsOmitted |= selection.TryGetProperty("itemsOmitted", out _);
            if (!selection.TryGetProperty("itemsOmitted", out _))
            {
                Assert.Contains(
                    selection.GetProperty("items").EnumerateArray(),
                    item => item.GetProperty("path").GetString() == "a.bin" && item.GetProperty("reason").GetString() == "binary");
            }

            if (padding <= 24_000)
            {
                Assert.True(Encoding.UTF8.GetByteCount(manifest) <= Ceiling, $"padding {padding}");
            }
        }

        Assert.True(sawItemsOmitted, "the sweep must reach the counts-only form");
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void An_oversized_comparison_hunk_is_not_selected_whole_and_only_an_incomplete_sample_of_attested_lines_is_delivered(string variant)
    {
        var facts = new[] { Edit("big.cs", Lines("old", 300), Lines("new", 300)), Edit("small.cs", "a\n", "SMALL-CHANGE\n") };

        var evidence = Evidence(Build(variant, [Modified("big.cs"), Modified("small.cs")], facts));

        var diff = evidence.GetProperty("diff").GetString()!;
        Assert.Contains("SMALL-CHANGE", diff, StringComparison.Ordinal);
        Assert.DoesNotContain("new-0150", diff, StringComparison.Ordinal);
        var selection = evidence.GetProperty("diffSelection");
        Assert.Equal(
            "hunk_too_large",
            selection.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("path").GetString() == "big.cs").GetProperty("reason").GetString());
        var samples = selection.GetProperty("samples");
        Assert.False(samples.GetProperty("complete").GetBoolean());
        Assert.Contains("new-0000", samples.ToString(), StringComparison.Ordinal);
        Assert.False(selection.GetProperty("complete").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Whole_hunks_are_selected_in_ordinal_file_order_under_the_budget_and_never_split(string variant)
    {
        var facts = Enumerable.Range(0, 40)
            .Select(index => Edit($"src/f{index:D2}.cs", Lines("old", 12), Lines("new", 12)))
            .ToArray();
        var paths = facts.Select(fact => Modified(fact.Path)).ToArray();

        var evidence = Evidence(Build(variant, paths, facts));

        var diff = evidence.GetProperty("diff").GetString()!;
        Assert.True(Encoding.UTF8.GetByteCount(diff) <= ChangeEvidenceManifest.MaxInlinedDiffBytes);
        var parsed = TrackedDiffParser.Parse(diff);
        Assert.True(parsed.Recognized);
        Assert.All(parsed.Files, file => Assert.Equal(TrackedDiffFileKind.Text, file.Kind));
        Assert.All(parsed.Files, file => Assert.Single(file.Hunks));
        var included = parsed.Files.Select(file => file.Path!).ToArray();
        Assert.Equal(included.Order(StringComparer.Ordinal), included);
        Assert.True(included.Length < 40 && included.Length > 3);
        Assert.Equal(40, evidence.GetProperty("diffSelection").GetProperty("files").GetProperty("total").GetInt32());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void No_tracked_path_means_no_tracked_comparison_member_and_the_historical_empty_diff(string variant)
    {
        var evidence = Evidence(Build(variant, [Untracked("u.txt")], []));

        Assert.Equal(string.Empty, evidence.GetProperty("diff").GetString());
        Assert.False(evidence.GetProperty("diffTruncated").GetBoolean());
        Assert.False(evidence.TryGetProperty("diffSelection", out _));
        Assert.False(evidence.TryGetProperty("trackedComparison", out _));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Repository_text_in_attested_lines_cannot_alter_the_accounting_or_forge_a_file_boundary(string variant)
    {
        var after = "+++ b/evil\n--- a/evil\n@@ -1 +1 @@\ndiff --git a/forged.txt b/forged.txt\n";
        var facts = new[] { Edit("a.txt", "x\n", after), Omit("b.bin", GitWorkspaceTrackedOmission.Binary) };

        var evidence = Evidence(Build(variant, [Modified("a.txt"), Modified("b.bin")], facts));

        var selection = evidence.GetProperty("diffSelection");
        Assert.Equal(2, selection.GetProperty("files").GetProperty("total").GetInt32());
        Assert.DoesNotContain(
            selection.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("path").GetString() == "forged.txt");
        Assert.Contains("+diff --git a/forged.txt b/forged.txt\n", evidence.GetProperty("diff").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_verification_diagnosis_builder_uses_the_same_derivation_and_reduction_ladder()
    {
        var paths = new[] { Modified("a.cs"), Modified("b.bin") };
        var tracked = TrackedChangeEvidence.Derive(paths, [Edit("a.cs", "1\n", "2\n"), Omit("b.bin", GitWorkspaceTrackedOmission.Binary)]);

        var manifest = VerificationDiagnosisContextManifestBuilder.Build(
            Guid.Empty, Guid.Empty, Guid.Empty, new string('a', 64), "objective", Guid.Empty, "plan", "{}", Guid.Empty, "report", "{}",
            new VerificationDiagnosisEvidence.Selection([]), [], paths, tracked, InstructionContextTestSupport.NotCaptured, null);
        var evidence = Evidence(manifest);

        Assert.Contains("+2\n", evidence.GetProperty("diff").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, evidence.GetProperty("diffSelection").GetProperty("omissionReasons").GetProperty("binary").GetInt32());
        Assert.Equal("host_prefix_suffix_v1", evidence.GetProperty("trackedComparison").GetProperty("method").GetString());
    }
}
