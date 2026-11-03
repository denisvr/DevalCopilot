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
