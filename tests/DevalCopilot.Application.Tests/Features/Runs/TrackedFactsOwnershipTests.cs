using System.Collections;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedFixture;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>R1 of the attested tracked-change review: the tracked facts crossing the Projects boundary are an owned, immutable
/// snapshot. Whatever collection a reader (or a double) hands in is copied when the result is constructed or replaced, and the
/// collection the result returns offers no mutation path, so text that was never attested cannot be swapped in between the reader's
/// physical proof and the manifest seal. A read-only wrapper over a caller-owned list would not satisfy this: every case below
/// mutates the caller's own collection or the returned one and then derives and seals.</summary>
public sealed class TrackedFactsOwnershipTests
{
    private const string Forged = "FORGED TEXT NEVER ATTESTED";

    private static readonly IReadOnlyList<GitWorkspaceChangedPath> Paths = [Modified("a.txt"), Modified("b.txt")];

    private static GitWorkspaceTrackedFile[] Originals() =>
        [Edit("a.txt", "before a\n", "after a\n"), Omit("b.txt", GitWorkspaceTrackedOmission.Binary)];

    private static GitWorkspaceEvidenceResult Result(IReadOnlyList<GitWorkspaceTrackedFile>? facts) =>
        new(GitWorkspaceEvidenceOutcome.Success, new string('a', 40), new string('b', 64), Paths, null, TrackedFiles: facts);

    private static void AssertStillTheOriginals(GitWorkspaceEvidenceResult result)
    {
        Assert.Equal(Originals(), result.TrackedFiles);
        var evidence = TrackedChangeEvidence.From(result);
        Assert.DoesNotContain(Forged, evidence.Text, StringComparison.Ordinal);
        Assert.Contains("+after a\n", evidence.Text, StringComparison.Ordinal);
        Assert.Equal(["b.txt:binary"], evidence.Omissions.Select(omission => $"{omission.Path}:{omission.Reason}"));
    }

    /// <summary>Every mutation route an untrusted holder of the returned collection could try; each may throw, none may succeed.</summary>
    private static void Tamper(object? collection)
    {
        var forged = Edit("a.txt", "before a\n", Forged);
        var routes = new Action[]
        {
            () => ((IList<GitWorkspaceTrackedFile>)collection!)[0] = forged,
            () => ((IList<GitWorkspaceTrackedFile>)collection!).Add(forged),
            () => ((IList<GitWorkspaceTrackedFile>)collection!).Insert(0, forged),
            () => ((IList<GitWorkspaceTrackedFile>)collection!).RemoveAt(1),
            () => ((IList<GitWorkspaceTrackedFile>)collection!).Clear(),
            () => ((ICollection<GitWorkspaceTrackedFile>)collection!).Clear(),
            () => ((IList)collection!)[0] = forged,
            () => ((IList)collection!).Clear(),
            () => ((GitWorkspaceTrackedFile[])collection!)[0] = forged,
            () => ((List<GitWorkspaceTrackedFile>)collection!)[0] = forged,
        };
        foreach (var route in routes)
        {
            try
            {
                route();
            }
            catch (Exception exception) when (exception is InvalidCastException or NotSupportedException or ArgumentException)
            {
                // Refused, as required.
            }
        }
    }

    [Fact]
    public void Mutating_the_callers_list_after_construction_changes_nothing_the_result_holds()
    {
        var callerList = new List<GitWorkspaceTrackedFile>(Originals());
        var result = Result(callerList);

        callerList[0] = Edit("a.txt", "before a\n", Forged);
        callerList.Add(Edit("c.txt", "x\n", Forged));
        callerList.Clear();

        AssertStillTheOriginals(result);
    }

    [Fact]
    public void Mutating_the_callers_array_after_construction_changes_nothing_the_result_holds()
    {
        var callerArray = Originals();
        var result = Result(callerArray);

        callerArray[0] = Edit("a.txt", "before a\n", Forged);
        callerArray[1] = Edit("b.txt", "x\n", Forged);

        AssertStillTheOriginals(result);
    }

    [Fact]
    public void The_returned_collection_offers_no_mutation_path_of_any_kind()
    {
        var result = Result(new List<GitWorkspaceTrackedFile>(Originals()));

        Tamper(result.TrackedFiles);

        AssertStillTheOriginals(result);
    }

    [Fact]
    public void A_result_over_an_array_returns_a_collection_that_is_not_that_array_nor_a_mutable_list()
    {
        var callerArray = Originals();
        var result = Result(callerArray);

        Assert.False(ReferenceEquals(callerArray, result.TrackedFiles));
        Assert.IsNotType<List<GitWorkspaceTrackedFile>>(result.TrackedFiles);
        Assert.False(result.TrackedFiles is GitWorkspaceTrackedFile[]);
        Tamper(result.TrackedFiles);
        AssertStillTheOriginals(result);
    }

    [Fact]
    public void Replacing_the_facts_with_the_with_expression_snapshots_the_new_collection_too()
    {
        var replacement = new List<GitWorkspaceTrackedFile>(Originals());
        var result = Result(null) with { TrackedFiles = replacement };

        replacement[0] = Edit("a.txt", "before a\n", Forged);
        replacement.Clear();
        Tamper(result.TrackedFiles);

        AssertStillTheOriginals(result);
    }

    [Fact]
    public void A_projected_or_copied_result_does_not_share_a_mutable_collection_with_its_source()
    {
        var callerList = new List<GitWorkspaceTrackedFile>(Originals());
        var source = Result(callerList);
        var copy = source with { FingerprintSha256 = new string('c', 64) };

        callerList[0] = Edit("a.txt", "before a\n", Forged);
        Tamper(source.TrackedFiles);
        Tamper(copy.TrackedFiles);

        AssertStillTheOriginals(source);
        AssertStillTheOriginals(copy);
    }

    [Fact]
    public void A_deconstructed_collection_is_as_immutable_as_the_property()
    {
        var result = Result(new List<GitWorkspaceTrackedFile>(Originals()));

        var (_, _, _, _, _, _, _, facts) = result;
        Tamper(facts);

        AssertStillTheOriginals(result);
    }

    [Fact]
    public void An_absent_attestation_stays_absent_and_an_empty_one_stays_empty()
    {
        Assert.Null(Result(null).TrackedFiles);
        Assert.Empty(Result([]).TrackedFiles!);
        Tamper(Result([]).TrackedFiles);
        Assert.Empty(Result([]).TrackedFiles!);
    }

    [Fact]
    public void A_null_fact_is_refused_at_the_boundary_instead_of_being_carried_inward()
    {
        Assert.Throws<ArgumentException>(() => Result(new GitWorkspaceTrackedFile[] { null! }));
    }

    [Fact]
    public void The_snapshot_keeps_the_order_the_values_and_the_count_of_what_was_handed_in()
    {
        var facts = new List<GitWorkspaceTrackedFile>(Originals());

        var result = Result(facts);

        Assert.Equal(2, result.TrackedFiles!.Count);
        Assert.Equal(Originals()[0], result.TrackedFiles[0]);
        Assert.Equal(Originals()[1], result.TrackedFiles[1]);
        Assert.Equal(Originals(), result.TrackedFiles.ToArray());
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void A_manifest_sealed_after_the_attempted_swap_delivers_the_attested_text_and_never_the_forged_one(string variant)
    {
        var callerList = new List<GitWorkspaceTrackedFile>(Originals());
        var result = Result(callerList);

        callerList[0] = Edit("a.txt", "before a\n", Forged);
        Tamper(result.TrackedFiles);
        var evidence = TrackedChangeEvidence.From(result);
        var manifest = UntrackedFileManifestTests.AttestedBuilder(variant)(Paths, evidence, null, 0);

        Assert.DoesNotContain(Forged, manifest, StringComparison.Ordinal);
        var diff = JsonDocument.Parse(manifest).RootElement.GetProperty("changeEvidence").GetProperty("diff").GetString()!;
        Assert.Contains("+after a\n", diff, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> Variants => UntrackedFileManifestTests.Variants;
}
