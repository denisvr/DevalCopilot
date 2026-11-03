using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R3 (correction 3): the approved Git is observed once, through the budgeted discovery.git class, before the first fixture
/// mutation, and must be a coherent version of at least 2.32.0. Doubles only: no real Git is probed by these tests.</summary>
public sealed class GitVersionGateTests : QualificationTestBase
{
    private static GitVersionObservation Ok(string output) => new(true, 0, false, false, output);

    public static TheoryData<string> Compatible => new()
    {
        { "git version 2.32.0" }, { "git version 2.32.0\n" }, { "git version 2.50.0.windows.1\r\n" }, { "git version 2.43.5" },
        { "git version 3.0.0" }, { "git version 10.1.2.windows.3" },
    };

    [Theory]
    [MemberData(nameof(Compatible))]
    public void A_coherent_version_of_at_least_the_minimum_is_compatible(string output)
    {
        Assert.Null(GitVersionGate.Evaluate(Ok(output)));
    }

    public static TheoryData<string> TooOld => new()
    {
        { "git version 2.31.1" }, { "git version 2.31.99.windows.1" }, { "git version 2.9.5" }, { "git version 2.0.0" },
        { "git version 1.9.0" }, { "git version 0.99.9" },
    };

    [Theory]
    [MemberData(nameof(TooOld))]
    public void An_older_version_is_refused_as_too_old(string output)
    {
        Assert.Equal("GitVersionTooOld", GitVersionGate.Evaluate(Ok(output)));
    }

    public static TheoryData<string> Unknown => new()
    {
        { "" }, { "git version" }, { "git version 2.x.0" }, { "git version 2.32" }, { "hello" }, { " git version 2.40.0" },
        { "git version 2.40.0 extra words" }, { "git version 2.40.0\ngit version 2.50.0" }, { "GIT VERSION 2.40.0" },
        { "git version 2.40.0\n\n" }, { "git version -2.40.0" }, { "git version 2.40.0." + (char)0 }, { "git version 99999.1.1" },
    };

    [Theory]
    [MemberData(nameof(Unknown))]
    public void An_unparseable_or_incoherent_version_is_refused_as_unknown(string output)
    {
        Assert.Equal("GitVersionUnknown", GitVersionGate.Evaluate(Ok(output)));
    }

    [Fact]
    public void A_failed_timed_out_unfinished_truncated_or_oversized_observation_is_refused_without_interpretation()
    {
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(new GitVersionObservation(true, 1, false, false, "git version 2.50.0")));
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(new GitVersionObservation(true, null, false, false, "git version 2.50.0")));
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(new GitVersionObservation(false, null, false, false, "git version 2.50.0")));
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(new GitVersionObservation(false, null, true, false, string.Empty)));
        Assert.Equal("GitVersionObservationFailed", GitVersionGate.Evaluate(new GitVersionObservation(true, 0, true, false, "git version 2.50.0")));
        Assert.Equal("GitVersionIncomplete", GitVersionGate.Evaluate(new GitVersionObservation(true, 0, false, true, "git version 2.50.0")));
        Assert.Equal("GitVersionIncomplete", GitVersionGate.Evaluate(Ok("git version 2.50.0." + new string('x', 600))));
    }

    [Fact]
    public void The_minimum_is_the_first_git_that_honors_the_global_configuration_variable()
    {
        Assert.Equal(new Version(2, 32, 0), GitVersionGate.Minimum);
    }

    // The admission of the observation -----------------------------------------------------------------------------------------

    private sealed class Observer(GitVersionObservation observation) : IGitVersionObserver
    {
        public int Calls { get; private set; }

        public string? ObservedPath { get; private set; }

        public IReadOnlyList<string> EnvironmentNames => ["SystemRoot"];

        public GitVersionObservation Observe(string gitPath, string workingDirectory, TimeSpan bound)
        {
            Calls++;
            ObservedPath = gitPath;
            return observation;
        }
    }

    private (ApprovedFixture Fixture, ProcessAdmission Admission) Compose() =>
        NewComposition(new ApprovedFixture(Path.Combine(LedgerDirectory, "approved"), codexScript: false));

    private (ApprovedFixture Fixture, ProcessAdmission Admission) NewComposition(ApprovedFixture fixture) =>
        (fixture, fixture.Admission(Path.Combine(LedgerDirectory, "counts")));

    [Fact]
    public void A_compatible_version_is_admitted_once_as_the_budgeted_discovery_git_class_for_the_exact_approved_file()
    {
        var (fixture, admission) = Compose();
        var observer = new Observer(Ok("git version 2.50.0.windows.1"));

        GitVersionGate.Require(admission, observer, fixture.DiscoveryScratch);

        Assert.Equal(1, observer.Calls);
        Assert.Equal(fixture.Git, observer.ObservedPath);
        Assert.Equal(1, admission.Facts.Admitted["discovery.git"]);
        Assert.Equal(1, admission.Facts.TotalAdmitted);
        Assert.Equal(1, admission.Ledger.Consumed()["discovery.git"]);
        Assert.Null(admission.Facts.ClosedBy);
        Assert.Equal(8, Profile01Inventory.DiscoveryPerCapability);
        Assert.Equal(179, admission.TotalLimit);
    }

    [Theory]
    [InlineData("git version 2.31.1", "GitVersionTooOld")]
    [InlineData("git version two", "GitVersionUnknown")]
    public void An_incompatible_version_refuses_with_its_closed_code_after_exactly_one_start(string output, string code)
    {
        var (fixture, admission) = Compose();
        var observer = new Observer(Ok(output));

        var refusal = Assert.Throws<GitCompatibilityException>(() => GitVersionGate.Require(admission, observer, fixture.DiscoveryScratch));

        Assert.Equal(code, refusal.Code);
        Assert.Equal(1, observer.Calls);
        Assert.Equal(1, admission.Facts.TotalAdmitted);
    }

    [Fact]
    public void An_admission_refusal_means_the_observer_never_runs_and_the_ceiling_still_applies()
    {
        var (fixture, admission) = Compose();
        var observer = new Observer(Ok("git version 2.50.0"));
        for (var index = 0; index < Profile01Inventory.DiscoveryPerCapability; index++)
        {
            GitVersionGate.Require(admission, observer, fixture.DiscoveryScratch);
        }

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => GitVersionGate.Require(admission, observer, fixture.DiscoveryScratch));

        Assert.Equal("ClassLimit.discovery.git", refusal.Code);
        Assert.Equal(8, observer.Calls);
    }

    [Fact]
    public void The_observation_shape_is_exactly_the_discovery_probe_so_any_other_shape_is_refused()
    {
        var (fixture, admission) = Compose();
        var observer = new Observer(Ok("git version 2.50.0"));
        var elsewhere = Path.Combine(fixture.Directory, "elsewhere");

        Assert.Throws<ProcessAdmissionRefusedException>(() => GitVersionGate.Require(admission, observer, elsewhere));

        Assert.Equal(0, observer.Calls);
        Assert.Equal(0, admission.Facts.TotalAdmitted);
    }

    [Fact]
    public void Without_an_approved_git_nothing_is_observed()
    {
        var judged = new JudgedTargets();
        var (entries, total) = Profile01Inventory.Create(judged);
        var admission = new ProcessAdmission(
            entries, total, new AdmissionLedger(Path.Combine(LedgerDirectory, "counts"), "owner-test-authorization", TimeProvider.System), judged.Revalidate)
        {
            LauncherGit = () => judged.LauncherGitPath,
        };
        var observer = new Observer(Ok("git version 2.50.0"));

        var refusal = Assert.Throws<GitCompatibilityException>(() => GitVersionGate.Require(admission, observer, LedgerDirectory));

        Assert.Equal("GitVersionNotApproved", refusal.Code);
        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public void A_replaced_approved_git_is_refused_by_the_identity_check_before_the_observation()
    {
        var (fixture, admission) = Compose();
        var observer = new Observer(Ok("git version 2.50.0"));
        ApprovedFixture.Tamper(fixture.Git);

        var refusal = Assert.Throws<ProcessAdmissionRefusedException>(() => GitVersionGate.Require(admission, observer, fixture.DiscoveryScratch));

        Assert.Equal("FileIdentityDrift", refusal.Code);
        Assert.Equal(0, observer.Calls);
    }
}
