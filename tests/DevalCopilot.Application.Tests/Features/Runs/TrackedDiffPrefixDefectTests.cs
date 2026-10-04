using DevalCopilot.Application.Features.Runs.Policies;
using System.Text.Json;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Commands.CreateClaudeCriticalReviewAttempt;
using Xunit;
using static DevalCopilot.Application.Tests.Features.Runs.TrackedDiffFixture;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class TrackedDiffPrefixDefectTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void A_large_first_file_does_not_hide_a_later_small_change()
    {
        var diff = TextFile("src/Large.cs", LargeHunk(1, 100, 'a'), LargeHunk(400, 100, 'c'))
            + TextFile("src/Small.cs", Hunk(3, " keep", "-old value", "+new value", " keep"));
        IReadOnlyList<GitWorkspaceChangedPath> paths =
            [new("src/Large.cs", null, " ", "M"), new("src/Small.cs", null, " ", "M")];

        var manifest = ClaudeCriticalReviewContextManifestBuilder.Build(
            Id, Id, Id, new string('a', 64), "objective", Id, "summary", "{}", paths, Composed(diff), InstructionContextTestSupport.NotCaptured);

        var included = JsonDocument.Parse(manifest).RootElement.GetProperty("changeEvidence").GetProperty("diff").GetString()!;
        Assert.Contains("+new value", included, StringComparison.Ordinal);
    }
}
