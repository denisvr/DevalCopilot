using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Marks a fact as requiring Windows — specifically, NTFS directory junction support, which has
/// no equivalent this suite relies on elsewhere. xUnit v2 has no dynamic <c>Assert.Skip</c>; this
/// is the standard v2 pattern for a platform-conditional skip that still shows as genuinely
/// Skipped (not silently Passed) in the test report, using only what <see cref="FactAttribute"/>
/// already exposes — no additional test-framework package.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows (NTFS directory junctions).";
        }
    }
}
