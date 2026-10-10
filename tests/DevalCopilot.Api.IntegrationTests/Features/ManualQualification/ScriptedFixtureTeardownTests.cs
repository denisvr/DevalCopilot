using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>
/// The rehearsal's fixture teardown has authority over one root only: the exact one its environment created, with the token it
/// generated then. Spies record what would be removed, so no test here deletes anything but the controlled roots it created
/// itself with its own tokens. A foreign root, a marker that agrees with itself and a native observation never reach removal.
/// </summary>
public sealed class ScriptedFixtureTeardownTests : IDisposable
{
    private readonly List<(string Path, string Token)> _controlled = [];

    public void Dispose()
    {
        foreach (var (path, token) in _controlled)
        {
            if (Directory.Exists(path))
            {
                OwnedRootCleaner.Remove(path, token);
            }
        }

        GC.SuppressFinalize(this);
    }

    private static QualificationReport Authorized() => new("teardown")
    {
        Shutdown = new ShutdownReport(true, false, "OriginUnresolved"),
        Cleanup = CleanupReport.Preserved("ShutdownUnproven"),
        Planner = new StageReading { ProcessOutcome = "Exited", ExitCode = 0 },
        Reviewer = new StageReading { ProcessOutcome = "Exited", ExitCode = 0 },
    };

    /// <summary>A real, marker-bearing root of this test's own, removed by this test with the token it chose.</summary>
    private string ControlledRoot(string markerContent, string ownToken)
    {
        var path = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), OwnedRootGuard.RootPrefix + "manual-xunit-controlled-" + Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(path, OwnedRootGuard.MarkerFile), markerContent);
        _controlled.Add((path, ownToken));
        return path;
    }

    private sealed class Spy
    {
        public List<(string Path, string Token)> Calls { get; } = [];

        public CleanupReport Remove(string path, string token)
        {
            Calls.Add((path, token));
            return CleanupReport.RemovedRoot;
        }
    }

    [Fact]
    public void A_valid_foreign_root_that_appears_after_the_start_is_never_selected_or_removed()
    {
        var owned = new OwnedRootReference(@"C:\owned-by-this-rehearsal", "held-token");
        var foreign = ControlledRoot("someone-elses-token", "someone-elses-token");
        var spy = new Spy();

        var result = ScriptedFixtureTeardown.TryRemove(owned, Authorized(), scriptedObservation: true, spy.Remove);

        Assert.NotNull(result);
        Assert.Equal([(owned.Path, owned.OwnerToken)], spy.Calls);
        Assert.DoesNotContain(spy.Calls, call => string.Equals(call.Path, foreign, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(foreign, OwnedRootGuard.MarkerFile)));
    }

    [Fact]
    public void The_token_is_the_one_held_since_creation_never_the_one_in_a_marker()
    {
        var root = ControlledRoot("marker-token", "marker-token");
        var held = new OwnedRootReference(root, "held-token");
        var spy = new Spy();

        ScriptedFixtureTeardown.TryRemove(held, Authorized(), scriptedObservation: true, spy.Remove);

        var call = Assert.Single(spy.Calls);
        Assert.Equal("held-token", call.Token);
        Assert.NotEqual(File.ReadAllText(Path.Combine(root, OwnedRootGuard.MarkerFile)), call.Token);
    }

    [Fact]
    public void A_marker_that_only_agrees_with_itself_is_no_authority_for_the_real_cleaner()
    {
        var root = ControlledRoot("self-consistent-marker", "self-consistent-marker");
        var held = new OwnedRootReference(root, "a-different-held-token");

        var result = ScriptedFixtureTeardown.TryRemove(held, Authorized(), scriptedObservation: true, OwnedRootCleaner.Remove);

        Assert.NotNull(result);
        Assert.False(result.Removed);
        Assert.Equal("OwnershipUnproven", result.Reason);
        Assert.True(Directory.Exists(root));
        Assert.True(File.Exists(Path.Combine(root, OwnedRootGuard.MarkerFile)));
    }

    [Fact]
    public void A_native_observation_never_reaches_removal_even_with_everything_else_established()
    {
        var spy = new Spy();

        var result = ScriptedFixtureTeardown.TryRemove(
            new OwnedRootReference(@"C:\owned", "held-token"), Authorized(), scriptedObservation: false, spy.Remove);

        Assert.Null(result);
        Assert.Empty(spy.Calls);
    }

    [Theory]
    [InlineData("no-root")]
    [InlineData("host-not-stopped")]
    [InlineData("no-shutdown-report")]
    [InlineData("planner-not-exited")]
    [InlineData("reviewer-not-exited")]
    [InlineData("planner-missing")]
    [InlineData("already-removed")]
    [InlineData("no-cleanup-decision")]
    public void Teardown_needs_every_fact_of_the_fixtures_own_shutdown(string missing)
    {
        var report = Authorized();
        var root = new OwnedRootReference(@"C:\owned", "held-token");
        switch (missing)
        {
            case "no-root":
                root = null!;
                break;
            case "host-not-stopped":
                report.Shutdown = new ShutdownReport(false, false, "Unproven");
                break;
            case "no-shutdown-report":
                report.Shutdown = null;
                break;
            case "planner-not-exited":
                report.Planner = new StageReading { ProcessOutcome = "TimedOut" };
                break;
            case "reviewer-not-exited":
                report.Reviewer = new StageReading { ProcessOutcome = null };
                break;
            case "planner-missing":
                report.Planner = null;
                break;
            case "already-removed":
                report.Cleanup = CleanupReport.RemovedRoot;
                break;
            case "no-cleanup-decision":
                report.Cleanup = null;
                break;
        }

        var spy = new Spy();

        var result = ScriptedFixtureTeardown.TryRemove(root, report, scriptedObservation: true, spy.Remove);

        Assert.Null(result);
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public void With_every_fact_established_the_exact_held_root_is_removed_through_the_real_cleaner()
    {
        var root = ControlledRoot("held-token", "held-token");
        File.WriteAllText(Path.Combine(root, "evidence.txt"), "fixture content");
        var held = new OwnedRootReference(root, "held-token");

        var result = ScriptedFixtureTeardown.TryRemove(held, Authorized(), scriptedObservation: true, OwnedRootCleaner.Remove);

        Assert.NotNull(result);
        Assert.True(result.Removed, result.Reason);
        Assert.False(Directory.Exists(root));
    }
}
