using System.Net;
using System.Text;
using System.Text.Json;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;
using static DevalCopilot.Api.IntegrationTests.Features.Projects.CheckpointInspectionScene;

namespace DevalCopilot.Api.IntegrationTests.Features.Projects;

/// <summary>
/// ADR-0027: through the real authenticated host, real Git, a real prepared workspace and real Windows hard links, the protected
/// checkpoint inspection route returns a bounded host comparison of attested tracked sources with explicit omissions and never
/// Git's raw working-path patch. Every assertion about the outside text inspects the ENTIRE response body, not one property.
/// </summary>
public sealed class CheckpointComparisonEndpointTests(HostCapabilityReadinessApiWebApplicationFactory factory)
    : IClassFixture<HostCapabilityReadinessApiWebApplicationFactory>
{
    private static readonly Dictionary<string, string> Baseline = new()
    {
        ["src/safe.txt"] = "alpha\nbeta\ngamma\n",
        ["src/linked.txt"] = "baseline linked\n",
        ["deep/nested/linked.txt"] = "baseline nested\n",
        ["linked-root.txt"] = "baseline root\n",
        ["second.txt"] = "baseline second\n",
        ["gone.txt"] = "to be deleted\n",
        ["AGENTS.md"] = "# Agents\nbaseline instructions\n",
        ["CLAUDE.md"] = "# Claude\nbaseline instructions\n",
        ["bin.dat"] = "baseline\n",
    };

    [Fact]
    public async Task An_unsafe_tracked_path_is_an_omission_beside_healthy_siblings_and_no_outside_text_reaches_the_response()
    {
        using var scene = await CreateAsync(factory, "inspection-unsafe", Baseline);
        scene.Write("src/safe.txt", "alpha\nBETA CHANGED\ngamma\n");
        scene.ReplaceWithOutsideHardLink("src/linked.txt");
        scene.ReplaceWithOutsideHardLink("deep/nested/linked.txt");
        scene.ReplaceWithOutsideHardLink("linked-root.txt");
        scene.ReplaceWithOutsideHardLink("CLAUDE.md");
        scene.Write("second.txt", "changed second\n");
        scene.AddInsideAlias("second.txt", "second-alias.txt");
        scene.Delete("gone.txt");
        scene.Write("added.txt", "brand new\n");
        scene.GitInWorkspace("add", "added.txt");
        scene.Write("AGENTS.md", "# Agents\nedited instructions\n");
        scene.WriteBytes("bin.dat", [1, 2, 0, 3]);
        var (checkpointId, fingerprint) = await scene.CaptureAsync();

        var (status, body) = await scene.InspectAsync(checkpointId);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("OUTSIDE-SENTINEL", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(fingerprint, root.GetProperty("fingerprintSha256").GetString());
        Assert.False(root.GetProperty("isComplete").GetBoolean());
        Assert.Equal(10, root.GetProperty("trackedPathCount").GetInt32());
        Assert.Equal(4, root.GetProperty("comparedPathCount").GetInt32());
        var omissions = root.GetProperty("omissions").EnumerateArray()
            .Select(item => (item.GetProperty("path").GetString()!, item.GetProperty("reason").GetString()!))
            .ToArray();
        Assert.Equal(
            [
                ("CLAUDE.md", "containment_unproven"),
                ("bin.dat", "binary"),
                ("deep/nested/linked.txt", "containment_unproven"),
                ("linked-root.txt", "containment_unproven"),
                ("second.txt", "containment_unproven"),
                ("src/linked.txt", "containment_unproven"),
            ],
            omissions);

        var text = root.GetProperty("comparisonText").GetString()!;
        Assert.Contains("diff --git a/src/safe.txt b/src/safe.txt\n", text, StringComparison.Ordinal);
        Assert.Contains("+BETA CHANGED\n", text, StringComparison.Ordinal);
        Assert.Contains("diff --git a/gone.txt b/gone.txt\n", text, StringComparison.Ordinal);
        Assert.Contains("+++ /dev/null\n", text, StringComparison.Ordinal);
        Assert.Contains("--- /dev/null\n+++ b/added.txt\n", text, StringComparison.Ordinal);
        Assert.Contains("+edited instructions\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("linked", text, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("limitation").GetString()));
        Assert.DoesNotContain("completeDiff", body, StringComparison.OrdinalIgnoreCase);

        // The untracked second name stays in the unchanged changed-files list as a name, never as a tracked comparison.
        Assert.Contains("second-alias.txt", await scene.ChangedFilesBodyAsync(checkpointId), StringComparison.Ordinal);
        Assert.DoesNotContain("second-alias.txt", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_capture_whose_every_tracked_path_is_unsafe_is_all_omitted_and_never_clean()
    {
        using var scene = await CreateAsync(factory, "inspection-all-omitted", Baseline);
        scene.ReplaceWithOutsideHardLink("src/linked.txt");
        scene.ReplaceWithOutsideHardLink("AGENTS.md");
        var (checkpointId, _) = await scene.CaptureAsync();

        var (status, body) = await scene.InspectAsync(checkpointId);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("OUTSIDE-SENTINEL", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(string.Empty, root.GetProperty("comparisonText").GetString());
        Assert.False(root.GetProperty("isComplete").GetBoolean());
        Assert.Equal(2, root.GetProperty("trackedPathCount").GetInt32());
        Assert.Equal(0, root.GetProperty("comparedPathCount").GetInt32());
        Assert.Equal(2, root.GetProperty("omissions").GetArrayLength());
    }

    [Fact]
    public async Task A_capture_with_no_tracked_change_is_a_genuinely_complete_empty_comparison()
    {
        using var scene = await CreateAsync(factory, "inspection-empty", Baseline);
        scene.Write("only-untracked.txt", "new untracked\n");
        var (checkpointId, _) = await scene.CaptureAsync();

        var (status, body) = await scene.InspectAsync(checkpointId);

        Assert.Equal(HttpStatusCode.OK, status);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(string.Empty, root.GetProperty("comparisonText").GetString());
        Assert.True(root.GetProperty("isComplete").GetBoolean());
        Assert.Equal(0, root.GetProperty("trackedPathCount").GetInt32());
        Assert.Equal(0, root.GetProperty("omissions").GetArrayLength());
        Assert.DoesNotContain("new untracked", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_route_stays_protected_and_checkpoint_membership_is_enforced()
    {
        using var scene = await CreateAsync(factory, "inspection-membership", Baseline);
        scene.Write("src/safe.txt", "alpha\nBETA CHANGED\ngamma\n");
        var (checkpointId, _) = await scene.CaptureAsync();
        using var anonymous = factory.CreateClient();
        var route = $"/api/projects/{scene.ProjectId}/workspace/checkpoints/{checkpointId}/diff";

        var unauthenticated = await anonymous.GetAsync(route);
        var (unknownCheckpoint, unknownBody) = await scene.InspectAsync(Guid.NewGuid());
        var otherProject = await scene.InspectOfProjectAsync(Guid.NewGuid(), checkpointId);

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.DoesNotContain("BETA CHANGED", await unauthenticated.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, unknownCheckpoint);
        Assert.Contains("git_checkpoints.not_found", unknownBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, otherProject.Status);
        Assert.DoesNotContain("BETA CHANGED", otherProject.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_checkpoint_is_refused_without_any_comparison_text()
    {
        using var scene = await CreateAsync(factory, "inspection-stale", Baseline);
        scene.Write("src/safe.txt", "stale baseline edit\n");
        var (checkpointId, _) = await scene.CaptureAsync();
        scene.Write("src/safe.txt", "edited after the checkpoint\n");
        scene.ReplaceWithOutsideHardLink("src/linked.txt");

        var (status, body) = await scene.InspectAsync(checkpointId);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("git_evidence.stale_checkpoint", body, StringComparison.Ordinal);
        Assert.DoesNotContain("OUTSIDE-SENTINEL", body, StringComparison.Ordinal);
        Assert.DoesNotContain("edited after the checkpoint", body, StringComparison.Ordinal);
        Assert.DoesNotContain("comparisonText", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("diff.noprefix", "true")]
    [InlineData("diff.mnemonicprefix", "true")]
    [InlineData("diff.external", "cmd /c echo REPOSITORY-DIFF-HELPER-RAN")]
    [InlineData("diff.srcprefix", "SRC-PREFIX/")]
    [InlineData("diff.dstprefix", "DST-PREFIX/")]
    public async Task Repository_diff_settings_never_change_the_host_comparison(string setting, string value)
    {
        using var scene = await CreateAsync(factory, "inspection-config-" + setting.Replace('.', '-'), Baseline);
        scene.GitInWorkspace("config", setting, value);
        scene.Write("src/safe.txt", "alpha\nBETA CHANGED\ngamma\n");
        var (checkpointId, _) = await scene.CaptureAsync();

        var (status, body) = await scene.InspectAsync(checkpointId);

        Assert.Equal(HttpStatusCode.OK, status);
        using var document = JsonDocument.Parse(body);
        var text = document.RootElement.GetProperty("comparisonText").GetString()!;
        Assert.StartsWith(
            "diff --git a/src/safe.txt b/src/safe.txt\n--- a/src/safe.txt\n+++ b/src/safe.txt\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("REPOSITORY-DIFF-HELPER-RAN", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PREFIX/", body, StringComparison.Ordinal);
        Assert.True(document.RootElement.GetProperty("isComplete").GetBoolean());
    }

    [Fact]
    public async Task A_textconv_attribute_never_changes_the_host_comparison()
    {
        using var scene = await CreateAsync(factory, "inspection-textconv", Baseline);
        scene.GitInWorkspace("config", "diff.evil.textconv", "cmd /c echo TEXTCONV-RAN");
        scene.Write(".gitattributes", "*.txt diff=evil\n");
        scene.Write("src/safe.txt", "alpha\nBETA CHANGED\ngamma\n");
        var (checkpointId, _) = await scene.CaptureAsync();

        var (_, body) = await scene.InspectAsync(checkpointId);

        Assert.DoesNotContain("TEXTCONV-RAN", body, StringComparison.Ordinal);
        Assert.Contains("+BETA CHANGED", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_comparison_text_is_bounded_and_an_over_budget_block_is_omitted_whole_with_honest_metadata()
    {
        var committed = new Dictionary<string, string>
        {
            ["a.txt"] = Lines("old", 3200),
            ["b.txt"] = Lines("old", 3200),
            ["c.txt"] = Lines("old", 1500),
        };
        using var scene = await CreateAsync(factory, "inspection-bounds", committed);
        scene.Write("a.txt", Lines("new-a", 3200));
        scene.Write("b.txt", Lines("new-b", 3200));
        scene.Write("c.txt", Lines("new-c", 1500));
        var (checkpointId, _) = await scene.CaptureAsync();

        var (status, body) = await scene.InspectAsync(checkpointId);

        Assert.Equal(HttpStatusCode.OK, status);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var text = root.GetProperty("comparisonText").GetString()!;
        Assert.True(Encoding.UTF8.GetByteCount(text) <= 512 * 1024);
        Assert.Contains("diff --git a/a.txt b/a.txt\n", text, StringComparison.Ordinal);
        Assert.Contains("diff --git a/b.txt b/b.txt\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("new-c", text, StringComparison.Ordinal);
        Assert.DoesNotContain("c.txt", text, StringComparison.Ordinal);
        Assert.False(root.GetProperty("isComplete").GetBoolean());
        Assert.Equal(3, root.GetProperty("trackedPathCount").GetInt32());
        Assert.Equal(2, root.GetProperty("comparedPathCount").GetInt32());
        var omission = Assert.Single(root.GetProperty("omissions").EnumerateArray());
        Assert.Equal("c.txt", omission.GetProperty("path").GetString());
        Assert.Equal("comparison_limit", omission.GetProperty("reason").GetString());
    }

    /// <summary>A source whose first and last lines carry the tag and whose middle lines never change: Git's own patch is small,
    /// while the host's one replacement hunk per file states the whole middle as removed and added.</summary>
    private static string Lines(string tag, int count) =>
        $"first-{tag}\n" + string.Concat(Enumerable.Range(0, count).Select(index => $"line-{index:D6}-{new string('x', 20)}\n"))
        + $"last-{tag}\n";
}
