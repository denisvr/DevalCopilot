using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>A fact that needs only Windows path and platform semantics. Unlike
/// <see cref="WindowsOnlyFactAttribute"/>, it never probes NTFS junction support, a process, the
/// filesystem or any permission: it checks the operating system and nothing else, and off Windows
/// it is reported as Skipped, never silently passed.</summary>
public sealed class WindowsPlatformFactAttribute : FactAttribute
{
    public WindowsPlatformFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows.";
        }
    }
}
