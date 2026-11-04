using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>The fresh authorization identity and its exclusive allowance location: no earlier ledger is reachable, named, read,
/// reset or archived by any entry, and the summary is never overwritten.</summary>
public sealed class FreshAuthorizationTests : QualificationTestBase
{
    // The earlier authorization's directory name, built here only to prove that it is refused. These tests never create, read or
    // enumerate any directory of that name.
    private static readonly string LegacyName = "devalcopilot-manual-" + "qualification-ledger";

    [Fact]
    public void The_default_ledger_is_the_fresh_authorizations_own_directory_and_never_the_earlier_one()
    {
        var ledger = InvocationLedger.CreateDefault();

        Assert.Equal(InvocationLedger.DirectoryName, Path.GetFileName(ledger.LedgerDirectory));
        Assert.NotEqual(LegacyName, Path.GetFileName(ledger.LedgerDirectory));
        Assert.Contains("qualification-profile-01-lot-b", ledger.LedgerDirectory, StringComparison.Ordinal);
        Assert.Contains("qualification-profile-01-lot-b", InvocationLedger.Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_lots_have_distinct_authorizations_and_distinct_directories()
    {
        Assert.NotEqual(InvocationLedger.Authorization, ClaudeStatusSummary.Authorization);
        Assert.NotEqual(InvocationLedger.DirectoryName, StatusComparisonEntry.LedgerDirectoryName);
        Assert.DoesNotContain(LegacyName, InvocationLedger.DirectoryName, StringComparison.Ordinal);
        Assert.DoesNotContain(LegacyName, StatusComparisonEntry.LedgerDirectoryName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ledger_pointed_at_the_earlier_directory_is_refused_at_construction_with_no_file_access(bool trailingSeparator)
    {
        var path = Path.Combine(LedgerDirectory, LegacyName) + (trailingSeparator ? Path.DirectorySeparatorChar : string.Empty);

        Assert.Throws<ArgumentException>(() => new InvocationLedger(path, TimeProvider.System));
        Assert.False(Directory.Exists(LedgerDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ledger_pointed_at_the_spent_first_lot_b_directory_is_refused_at_construction_with_no_file_access(bool trailingSeparator)
    {
        var path = Path.Combine(LedgerDirectory, InvocationLedger.RetiredDirectoryName)
            + (trailingSeparator ? Path.DirectorySeparatorChar : string.Empty);

        Assert.Throws<ArgumentException>(() => new InvocationLedger(path, TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new InvocationLedger(path.ToUpperInvariant(), TimeProvider.System));
        Assert.False(Directory.Exists(LedgerDirectory));
    }

    [Fact]
    public void The_replacement_ledger_is_a_different_directory_and_authorization_from_the_spent_first_lot_b_one()
    {
        Assert.Equal("devalcopilot-qualification-profile-01-lot-b-ledger", InvocationLedger.RetiredDirectoryName);
        Assert.NotEqual(InvocationLedger.RetiredDirectoryName, InvocationLedger.DirectoryName);
        Assert.NotEqual(InvocationLedger.RetiredDirectoryName, Path.GetFileName(InvocationLedger.CreateDefault().LedgerDirectory));
        Assert.NotEqual("owner-qualification-profile-01-lot-b-one-planner-one-critical-reviewer", InvocationLedger.Authorization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ledger_pointed_at_the_spent_context_bound_directory_is_refused_at_construction_with_no_file_access(
        bool trailingSeparator)
    {
        var path = Path.Combine(LedgerDirectory, InvocationLedger.RetiredContextBoundDirectoryName)
            + (trailingSeparator ? Path.DirectorySeparatorChar : string.Empty);

        Assert.Throws<ArgumentException>(() => new InvocationLedger(path, TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new InvocationLedger(path.ToUpperInvariant(), TimeProvider.System));
        Assert.False(Directory.Exists(LedgerDirectory));
    }

    [Fact]
    public void The_successor_ledger_is_a_different_directory_and_authorization_from_the_spent_context_bound_one()
    {
        Assert.Equal("devalcopilot-qualification-profile-01-lot-b-context-bound-ledger", InvocationLedger.RetiredContextBoundDirectoryName);
        Assert.NotEqual(InvocationLedger.RetiredContextBoundDirectoryName, InvocationLedger.DirectoryName);
        Assert.NotEqual(
            InvocationLedger.RetiredContextBoundDirectoryName, Path.GetFileName(InvocationLedger.CreateDefault().LedgerDirectory));
        Assert.NotEqual("owner-qualification-profile-01-lot-b-context-bound", InvocationLedger.Authorization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ledger_pointed_at_the_spent_windows_root_directory_is_refused_at_construction_with_no_file_access(
        bool trailingSeparator)
    {
        var path = Path.Combine(LedgerDirectory, InvocationLedger.RetiredWindowsRootDirectoryName)
            + (trailingSeparator ? Path.DirectorySeparatorChar : string.Empty);

        Assert.Throws<ArgumentException>(() => new InvocationLedger(path, TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new InvocationLedger(path.ToUpperInvariant(), TimeProvider.System));
        Assert.False(Directory.Exists(LedgerDirectory));
    }

    [Fact]
    public void The_successor_ledger_is_a_different_directory_and_authorization_from_the_spent_windows_root_one()
    {
        Assert.Equal("devalcopilot-qualification-profile-01-lot-b-windows-root-ledger", InvocationLedger.RetiredWindowsRootDirectoryName);
        Assert.NotEqual(InvocationLedger.RetiredWindowsRootDirectoryName, InvocationLedger.DirectoryName);
        Assert.NotEqual(
            InvocationLedger.RetiredWindowsRootDirectoryName, Path.GetFileName(InvocationLedger.CreateDefault().LedgerDirectory));
        Assert.NotEqual("owner-qualification-profile-01-lot-b-windows-root", InvocationLedger.Authorization);
        Assert.Equal("owner-qualification-profile-01-lot-b-review-schema", InvocationLedger.Authorization);
        Assert.Equal("devalcopilot-qualification-profile-01-lot-b-review-schema-ledger", Path.GetFileName(
            InvocationLedger.CreateDefault().LedgerDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ledger_pointed_at_the_spent_proposal_guidance_directory_is_refused_at_construction_with_no_file_access(
        bool trailingSeparator)
    {
        var path = Path.Combine(LedgerDirectory, InvocationLedger.RetiredProposalGuidanceDirectoryName)
            + (trailingSeparator ? Path.DirectorySeparatorChar : string.Empty);

        Assert.Throws<ArgumentException>(() => new InvocationLedger(path, TimeProvider.System));
        Assert.Throws<ArgumentException>(() => new InvocationLedger(path.ToUpperInvariant(), TimeProvider.System));
        Assert.False(Directory.Exists(LedgerDirectory));
    }

    [Fact]
    public void The_successor_ledger_is_a_different_directory_and_authorization_from_the_spent_proposal_guidance_one()
    {
        Assert.Equal(
            "devalcopilot-qualification-profile-01-lot-b-proposal-guidance-ledger",
            InvocationLedger.RetiredProposalGuidanceDirectoryName);
        Assert.NotEqual(InvocationLedger.RetiredProposalGuidanceDirectoryName, InvocationLedger.DirectoryName);
        Assert.NotEqual(
            InvocationLedger.RetiredProposalGuidanceDirectoryName, Path.GetFileName(InvocationLedger.CreateDefault().LedgerDirectory));
        Assert.NotEqual("owner-qualification-profile-01-lot-b-proposal-guidance", InvocationLedger.Authorization);
    }

    [Fact]
    public void No_switch_exists_that_selects_an_authorization_or_directory_for_the_launcher()
    {
        var overloads = typeof(LauncherEntry).GetMethods().Where(method => method.Name == nameof(LauncherEntry.RunAsync)).ToArray();
        Assert.NotEmpty(overloads);
        var names = overloads.SelectMany(overload => overload.GetParameters()).Select(parameter => parameter.Name).ToArray();

        Assert.DoesNotContain(names, name => name is not null && name.Contains("authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name is not null && name.Contains("directory", StringComparison.OrdinalIgnoreCase));
    }
}
