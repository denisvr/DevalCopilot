using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Marks a fact as requiring the ability to create a file-level NTFS symbolic link — something
/// only available with elevation or Windows' Developer Mode, and with no non-elevated
/// alternative (NTFS junctions, used elsewhere in this suite, are directory-only). Whether this
/// host can do it is only knowable by actually attempting it, so this attribute probes once, at
/// test discovery, in its own disposable scratch directory, and sets the inherited
/// <see cref="FactAttribute.Skip"/> property when it cannot — the same v2-native mechanism
/// <see cref="WindowsOnlyFactAttribute"/> uses, so a genuinely unsupported host shows as Skipped,
/// never as a silently-passed assertion that never actually ran.
/// </summary>
public sealed class RequiresFileSymlinkSupportFactAttribute : FactAttribute
{
    public RequiresFileSymlinkSupportFactAttribute()
    {
        if (!CanCreateFileSymbolicLinks())
        {
            Skip = "This host cannot create a file-level symbolic link without elevation or " +
                   "Developer Mode, and NTFS junctions are directory-only, so there is no " +
                   "non-elevated alternative fixture for a single-file reparse point.";
        }
    }

    private static bool CanCreateFileSymbolicLinks()
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), $"devalcopilot-symlink-capability-probe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(probeDirectory);
        try
        {
            var target = Path.Combine(probeDirectory, "target.tmp");
            var link = Path.Combine(probeDirectory, "link.tmp");
            File.WriteAllText(target, string.Empty);
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(probeDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of a throwaway discovery-time probe directory.
            }
        }
    }
}
