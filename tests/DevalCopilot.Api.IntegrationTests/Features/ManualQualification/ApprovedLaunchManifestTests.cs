using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.ManualQualification;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ManualQualification;

/// <summary>QP-R3: the owner's approved-launch manifest and the lot B opt-in that carries it. Owned stand-in files only; no provider is
/// inspected and no installed tool is read.</summary>
public sealed class ApprovedLaunchManifestTests : QualificationTestBase
{
    private ApprovedFixture Fixture(bool codexScript = true) => new(Path.Combine(LedgerDirectory, "approved"), codexScript);

    private static object Entry(ApprovedLaunch launch)
    {
        var entry = new Dictionary<string, string>
        {
            ["capability"] = launch.Capability,
            ["path"] = launch.Executable.Path,
            ["sha256"] = launch.Executable.Sha256,
        };
        if (launch.Script is { } script)
        {
            entry["scriptPath"] = script.Path;
            entry["scriptSha256"] = script.Sha256;
        }

        return entry;
    }

    private static string Json(ApprovedFixture fixture, Func<List<object>, List<object>>? change = null) =>
        JsonSerializer.Serialize(new { approvedLaunches = (change ?? (list => list))([.. fixture.Set.Launches.Select(Entry)]) });

    private string Write(string json)
    {
        Directory.CreateDirectory(LedgerDirectory);
        var path = Path.Combine(LedgerDirectory, "approved-launches.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void A_complete_manifest_of_exact_files_loads_the_verified_set_with_the_node_script_tuple()
    {
        var fixture = Fixture();

        var (set, refusal) = ApprovedLaunchManifest.Load(Write(Json(fixture)));

        Assert.Null(refusal);
        Assert.NotNull(set);
        Assert.Equal(fixture.Git, set.GitLaunch.Executable.Path);
        Assert.Equal(fixture.CodexScript, set.CodexLaunch.Script!.Path);
        Assert.Equal(fixture.Node, set.CodexLaunch.Executable.Path);
        Assert.Equal(7, set.Launches.Count);
    }

    [Fact]
    public void A_manifest_with_only_the_required_capabilities_is_complete_and_optional_ones_are_simply_absent()
    {
        var fixture = new ApprovedFixture(Path.Combine(LedgerDirectory, "approved"), codexScript: false, withOptional: false);

        var (set, refusal) = ApprovedLaunchManifest.Parse(Json(fixture));

        Assert.Null(refusal);
        Assert.Null(set!.Find(ApprovedLaunchSet.Docker));
        Assert.NotNull(set.Find(ApprovedLaunchSet.Git));
    }

    [Fact]
    public void A_file_that_changed_after_it_was_approved_refuses_the_whole_manifest_with_the_closed_drift_code()
    {
        var fixture = Fixture();
        var path = Write(Json(fixture));
        ApprovedFixture.Tamper(fixture.Claude);

        var (set, refusal) = ApprovedLaunchManifest.Load(path);

        Assert.Null(set);
        Assert.Equal("FileIdentityDrift", refusal);
    }

    [Fact]
    public void A_listed_file_that_does_not_exist_refuses_the_manifest()
    {
        var fixture = Fixture();
        var path = Write(Json(fixture));
        File.Delete(fixture.Git);

        Assert.Equal("FileIdentityNotARealFile", ApprovedLaunchManifest.Load(path).Refusal);
    }

    public static TheoryData<string> Malformed => new()
    {
        { "notJson" }, { "array" }, { "extraRootMember" }, { "missingRoot" }, { "rootNotArray" }, { "entryNotObject" },
        { "unknownMember" }, { "duplicateMember" }, { "numberPath" }, { "scriptWithoutDigest" }, { "digestWithoutScript" },
        { "missingCapability" }, { "emptyList" }, { "uppercaseDigest" }, { "relativePath" }, { "dottedPath" },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void A_malformed_manifest_is_refused_with_a_closed_code_and_starts_nothing(string kind)
    {
        var fixture = Fixture();
        var git = fixture.Set.GitLaunch.Executable;
        var json = kind switch
        {
            "notJson" => "{not json",
            "array" => "[]",
            "extraRootMember" => "{\"approvedLaunches\":[],\"other\":1}",
            "missingRoot" => "{}",
            "rootNotArray" => "{\"approvedLaunches\":{}}",
            "entryNotObject" => "{\"approvedLaunches\":[1]}",
            "unknownMember" => Json(fixture, list => { ((Dictionary<string, string>)list[0])["extra"] = "x"; return list; }),
            "duplicateMember" => "{\"approvedLaunches\":[{\"capability\":\"git\",\"capability\":\"git\",\"path\":\"" + git.Path.Replace("\\", "\\\\", StringComparison.Ordinal) + "\",\"sha256\":\"" + git.Sha256 + "\"}]}",
            "numberPath" => "{\"approvedLaunches\":[{\"capability\":\"git\",\"path\":5,\"sha256\":\"" + git.Sha256 + "\"}]}",
            "scriptWithoutDigest" => Json(fixture, list => { ((Dictionary<string, string>)list[0])["scriptPath"] = git.Path; return list; }),
            "digestWithoutScript" => Json(fixture, list => { ((Dictionary<string, string>)list[0])["scriptSha256"] = git.Sha256; return list; }),
            "missingCapability" => Json(fixture, list => { ((Dictionary<string, string>)list[0]).Remove("capability"); return list; }),
            "emptyList" => "{\"approvedLaunches\":[]}",
            "uppercaseDigest" => Json(fixture, list => { ((Dictionary<string, string>)list[0])["sha256"] = git.Sha256.ToUpperInvariant(); return list; }),
            "relativePath" => Json(fixture, list => { ((Dictionary<string, string>)list[0])["path"] = "git.exe"; return list; }),
            _ => Json(fixture, list => { ((Dictionary<string, string>)list[0])["path"] = Path.Combine(Path.GetDirectoryName(git.Path)!, "..", "tools", "git.exe"); return list; }),
        };

        var (set, refusal) = ApprovedLaunchManifest.Parse(json);

        Assert.Null(set);
        Assert.False(string.IsNullOrEmpty(refusal));
        Assert.DoesNotContain(fixture.Directory, refusal!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_manifest_file_itself_must_be_an_exact_regular_bounded_file()
    {
        var fixture = Fixture();
        var path = Write(Json(fixture));

        Assert.Equal("ManifestPathNotExact", ApprovedLaunchManifest.Load("approved-launches.json").Refusal);
        Assert.Equal("ManifestPathNotExact", ApprovedLaunchManifest.Load(Path.Combine(Path.GetDirectoryName(path)!, "..", Path.GetFileName(Path.GetDirectoryName(path)!), "approved-launches.json")).Refusal);
        Assert.Equal("ManifestNotARealFile", ApprovedLaunchManifest.Load(Path.Combine(LedgerDirectory, "missing.json")).Refusal);
        var oversized = Path.Combine(LedgerDirectory, "oversized.json");
        File.WriteAllText(oversized, new string(' ', (int)ApprovedLaunchManifest.MaximumBytes + 1));
        Assert.Equal("ManifestSizeUnexpected", ApprovedLaunchManifest.Load(oversized).Refusal);
    }

    // The opt-in that carries the manifest --------------------------------------------------------------------------------------

    private static string? Confirmed(string name) =>
        name == QualificationOptIn.ConfirmationVariable ? QualificationOptIn.ConfirmationPhrase : null;

    [Fact]
    public void The_opt_in_accepts_the_flag_alone_or_with_exactly_one_manifest_option_and_path()
    {
        Assert.True(QualificationOptIn.IsGranted([QualificationOptIn.Flag], Confirmed));
        Assert.True(QualificationOptIn.IsGranted([QualificationOptIn.Flag, ApprovedLaunchManifest.Option, @"C:\owner\approved.json"], Confirmed));
        Assert.Equal(@"C:\owner\approved.json", QualificationOptIn.ApprovedLaunchesPath([QualificationOptIn.Flag, ApprovedLaunchManifest.Option, @"C:\owner\approved.json"]));
        Assert.Null(QualificationOptIn.ApprovedLaunchesPath([QualificationOptIn.Flag]));
    }

    [Theory]
    [InlineData("option-only")]
    [InlineData("other-option")]
    [InlineData("empty-path")]
    [InlineData("extra")]
    [InlineData("two")]
    public void Any_other_shape_of_the_manifest_arguments_is_refused(string defect)
    {
        string[] arguments = defect switch
        {
            "option-only" => [QualificationOptIn.Flag, ApprovedLaunchManifest.Option],
            "other-option" => [QualificationOptIn.Flag, "--approved", @"C:\owner\approved.json"],
            "empty-path" => [QualificationOptIn.Flag, ApprovedLaunchManifest.Option, string.Empty],
            "extra" => [QualificationOptIn.Flag, ApprovedLaunchManifest.Option, @"C:\owner\approved.json", "--again"],
            _ => [QualificationOptIn.Flag, ApprovedLaunchManifest.Option, @"C:\a.json", ApprovedLaunchManifest.Option, @"C:\b.json"],
        };

        Assert.False(QualificationOptIn.IsGranted(arguments, Confirmed));
    }
}
