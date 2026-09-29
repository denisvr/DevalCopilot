using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>A theory that needs Windows semantics (physical-containment proof, NTFS behavior). On any
/// other host it is reported as Skipped, never silently passed.</summary>
public sealed class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows.";
        }
    }
}
