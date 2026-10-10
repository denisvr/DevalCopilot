using System.Security.Cryptography;
using DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public static class SealedArtifactFile
{
    /// <summary>The sealed file's bytes when it was captured whole, lies strictly inside the owned artifact root with no alias on its
    /// path, is a plain file, and its length and SHA-256 equal the recorded ones; otherwise null. Nothing is written or kept.</summary>
    public static byte[]? ReadVerified(
        string artifactRoot,
        string relativeStoragePath,
        long byteLength,
        string contentHash,
        bool captured)
    {
        if (!captured || Path.IsPathRooted(relativeStoragePath))
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(artifactRoot, relativeStoragePath));
        var plainFileInside = OwnedLocation.IsInside(artifactRoot, path)
            && !OwnedLocation.HasReparsePoint(path, artifactRoot)
            && File.Exists(path);
        if (!plainFileInside)
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        var actualHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        return bytes.LongLength == byteLength && string.Equals(actualHash, contentHash, StringComparison.Ordinal)
            ? bytes
            : null;
    }
}
