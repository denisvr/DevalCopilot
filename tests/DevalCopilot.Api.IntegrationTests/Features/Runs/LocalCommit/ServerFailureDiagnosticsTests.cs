using System.Net;
using DevalCopilot.Application.Features.Runs.Ports;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>The failure diagnostics of the concurrent local-commit tests describe a real server failure from its own response and the
/// host's logged exception, bounded and without secrets, and stay silent when nothing failed.</summary>
public sealed class ServerFailureDiagnosticsTests : LocalCommitTestBase
{
    private const string Secret = "launch-secret-value";

    private static CapturedServerErrors NewCapture() => new([Secret], [(@"C:\scene-root", "<scene>")]);

    [Fact]
    public void Captured_text_never_contains_a_secret_or_a_known_root_or_a_foreign_directory()
    {
        var capture = NewCapture();
        var logger = capture.CreateLogger("Test");

        logger.LogError(
            new InvalidOperationException($"boom {Secret} at C:\\scene-root\\work\\a.txt and C:\\Other\\Place\\deep\\b.cs"),
            "failed {Detail}",
            "detail");

        var described = capture.Describe();
        Assert.DoesNotContain(Secret, described, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\scene-root", described, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Other", described, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<secret>", described, StringComparison.Ordinal);
        Assert.Contains("<scene>", described, StringComparison.Ordinal);
        Assert.Contains("b.cs", described, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_entry_and_a_response_body_are_bounded()
    {
        var capture = NewCapture();
        capture.CreateLogger("Test").LogError("failed {Detail}", new string('x', 5000));

        Assert.InRange(capture.Describe().Length, 1, 2000);
        Assert.InRange(capture.Sanitize(new string('y', 5000), 1200).Length, 1200, 1300);
        Assert.Contains("more characters omitted", capture.Sanitize(new string('y', 5000), 1200), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_first_errors_are_kept_and_lower_levels_are_ignored()
    {
        var capture = NewCapture();
        var logger = capture.CreateLogger("Test");

        logger.LogWarning("a warning");
        for (var index = 0; index < 20; index++)
        {
            logger.LogError("error number {Index}", index);
        }

        var described = capture.Describe();
        Assert.DoesNotContain("a warning", described, StringComparison.Ordinal);
        Assert.Contains("error number 0", described, StringComparison.Ordinal);
        Assert.DoesNotContain("error number 6", described, StringComparison.Ordinal);
        Assert.Equal(6, described.Split("\n---\n").Length);
    }

    [Fact]
    public void A_host_that_logged_no_error_says_so()
    {
        Assert.Equal("the host logged no error", NewCapture().Describe());
    }

    [Fact]
    public async Task An_unexpected_server_failure_is_described_from_its_response_and_logged_exception_without_the_launch_secret()
    {
        using var host = StartHost(runSupervisor: false, decoratePreparer: _ => new ThrowingPreparer());
        var ids = await LocalCommitLineage.SeedAsync(host, Scene);

        using var response = await PostAsync(host, ids.RunId, ids);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var described = await host.ServerErrors.DescribeAsync(response);
        Assert.StartsWith("500 InternalServerError;", described, StringComparison.Ordinal);
        Assert.Contains("The preparer failed on purpose", described, StringComparison.Ordinal);
        Assert.DoesNotContain(LocalCommitHost.Secret, described, StringComparison.Ordinal);
        Assert.DoesNotContain(Scene.Root, described, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(described.Length, 1, 6000);
        Assert.Equal(0, await OperationCountAsync(host));
    }

    private sealed class ThrowingPreparer : ILocalCommitPreparer
    {
        public Task<LocalCommitPreparationResult> PrepareAsync(LocalCommitPreparationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"The preparer failed on purpose near {request.WorkspacePath}.");
    }
}
