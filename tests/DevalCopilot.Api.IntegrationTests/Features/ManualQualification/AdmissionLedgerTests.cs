using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

public sealed class AdmissionLedgerTests : QualificationTestBase
{
    private AdmissionLedger Counters(string name = "counters") =>
        new(Path.Combine(LedgerDirectory, name), "owner-test-authorization", TimeProvider.System);

    [Fact]
    public void A_class_slot_is_taken_only_up_to_its_limit_and_each_slot_is_a_durable_file()
    {
        var counters = Counters();

        Assert.Null(counters.TryAdmit("role.codex.exec", 2, 10));
        Assert.Null(counters.TryAdmit("role.codex.exec", 2, 10));
        Assert.Equal("ClassLimit", counters.TryAdmit("role.codex.exec", 2, 10));

        var files = Directory.GetFiles(counters.LedgerDirectory, "admission-role.codex.exec-*.start");
        Assert.Equal(2, files.Length);
        Assert.Equal(2, counters.Consumed()["role.codex.exec"]);
        Assert.Equal(2, counters.Consumed()[AdmissionLedger.TotalName]);
    }

    [Fact]
    public void The_total_refuses_even_when_the_class_still_has_room_and_never_refunds_the_taken_class_slot()
    {
        var counters = Counters();
        Assert.Null(counters.TryAdmit("discovery.git", 8, 1));

        Assert.Equal("TotalLimit", counters.TryAdmit("discovery.node", 8, 1));

        Assert.Equal(1, counters.Consumed()["discovery.node"]);
        Assert.Equal(1, counters.Consumed()[AdmissionLedger.TotalName]);
    }

    [Fact]
    public void A_restart_or_a_second_launcher_over_the_same_directory_finds_every_slot_spent()
    {
        Assert.Null(Counters().TryAdmit("role.claude.print", 1, 5));

        var secondLauncher = Counters();

        Assert.Equal("ClassLimit", secondLauncher.TryAdmit("role.claude.print", 1, 5));
        Assert.True(secondLauncher.AnyConsumed());
    }

    [Fact]
    public void A_different_authorization_directory_has_its_own_untouched_counters()
    {
        Assert.Null(Counters("one").TryAdmit("role.claude.print", 1, 5));

        Assert.Null(Counters("two").TryAdmit("role.claude.print", 1, 5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Role.Upper")]
    [InlineData("has space")]
    [InlineData("a/b")]
    [InlineData("total")]
    public void An_invalid_or_reserved_class_name_is_refused_without_creating_a_slot(string name)
    {
        var counters = Counters();

        Assert.Equal("InventoryInvalid", counters.TryAdmit(name, 1, 1));

        Assert.False(Directory.Exists(counters.LedgerDirectory));
    }

    [Fact]
    public void A_ledger_directory_that_is_an_alias_is_unwritable_and_creates_nothing_through_it()
    {
        var real = Directory.CreateDirectory(Path.Combine(LedgerDirectory, "real")).FullName;
        var link = Path.Combine(LedgerDirectory, "link");
        try
        {
            Directory.CreateSymbolicLink(link, real);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var counters = new AdmissionLedger(link, "owner-test-authorization", TimeProvider.System);

        Assert.Equal("LedgerUnwritable", counters.TryAdmit("role.claude.print", 1, 1));
        Assert.Empty(Directory.GetFiles(real));
    }

    [Fact]
    public void Nothing_is_ever_deleted_or_rewritten_by_further_admissions()
    {
        var counters = Counters();
        Assert.Null(counters.TryAdmit("discovery.git", 3, 9));
        var first = Directory.GetFiles(counters.LedgerDirectory, "admission-discovery.git-1.start").Single();
        var before = File.ReadAllBytes(first);

        Assert.Null(counters.TryAdmit("discovery.git", 3, 9));
        Assert.Null(counters.TryAdmit("discovery.git", 3, 9));
        Assert.Equal("ClassLimit", counters.TryAdmit("discovery.git", 3, 9));

        Assert.Equal(before, File.ReadAllBytes(first));
        Assert.Equal(3, Directory.GetFiles(counters.LedgerDirectory, "admission-discovery.git-*.start").Length);
    }
}
