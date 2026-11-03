using System.Security.Cryptography;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The bounded identity check of the one Claude executable the owner approved, made before any start: an absolute, normalized path
/// to a real (non-alias) file named <c>claude.exe</c> of a sane size whose SHA-256 equals the owner's pinned digest. Nothing is
/// searched, resolved through a profile or cache, or substituted, and only the file's own bytes are read.
/// </summary>
public static class ClaudeBinaryVerifier
{
    public const string ExecutableName = "claude.exe";
    public const long MaximumBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>The verified facts, or the closed refusal code.</summary>
    public static async Task<(ClaudeBinaryFacts? Facts, string? Refusal)> VerifyAsync(
        string path,
        string pinnedSha256,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)
                || !string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(path), ExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                return (null, "BinaryPathNotExact");
            }

            var file = new FileInfo(path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.LinkTarget is not null)
            {
                return (null, "BinaryNotARealFile");
            }

            if (file.Length is < 1 or > MaximumBytes)
            {
                return (null, "BinarySizeUnexpected");
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            return string.Equals(digest, pinnedSha256, StringComparison.Ordinal)
                ? (new ClaudeBinaryFacts(true, file.Length, digest), null)
                : (null, "BinaryDigestMismatch");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, "BinaryUnreadable");
        }
    }
}
