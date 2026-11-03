using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// One file the owner approved by exact absolute path and SHA-256 digest. <see cref="Verify"/> reads only that file's own bytes and
/// returns a closed refusal when it is not exactly that regular, non-alias, bounded file. It is checked when the approval is loaded
/// and again immediately before every admitted start that launches the file, so a replaced file is refused rather than launched.
/// It proves only that the bytes matched at that moment: it is no protection against an executable that the same user can alter
/// between the check and the launch, or against a malicious but approved file.
/// </summary>
public sealed partial record ApprovedFile(string Path, string Sha256)
{
    public const long MaximumBytes = 1L * 1024 * 1024 * 1024;

    public static bool IsSha256(string value) => DigestPattern().IsMatch(value);

    /// <summary>True when <paramref name="other"/> is the same absolute file (case-insensitively, as on Windows).</summary>
    public bool IsPath(string? other) =>
        other is not null && string.Equals(Path, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>The closed refusal code, or null when the file is exactly the approved one.</summary>
    public string? Verify()
    {
        try
        {
            if (!System.IO.Path.IsPathFullyQualified(Path)
                || !string.Equals(System.IO.Path.GetFullPath(Path), Path, StringComparison.OrdinalIgnoreCase)
                || !IsSha256(Sha256))
            {
                return "FileIdentityNotExact";
            }

            var file = new FileInfo(Path);
            if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.LinkTarget is not null)
            {
                return "FileIdentityNotARealFile";
            }

            if (file.Length is < 1 or > MaximumBytes)
            {
                return "FileIdentitySizeUnexpected";
            }

            using var stream = new FileStream(
                Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            var digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            return string.Equals(digest, Sha256, StringComparison.Ordinal) ? null : "FileIdentityDrift";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "FileIdentityUnreadable";
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex DigestPattern();
}
