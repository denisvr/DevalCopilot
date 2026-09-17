using System.Text;
using System.Text.Json;

namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// The bounded, closed set of outcomes from one <see cref="PackageEntrypointResolver.Resolve"/>
/// call.
/// </summary>
internal enum PackageEntrypointResolutionKind
{
    /// <summary>No candidate root yielded a valid, existing, reparse-point-free entrypoint (or
    /// the entrypoint resolved but no direct <c>node.exe</c> was found to run it).</summary>
    NotFound,

    /// <summary>Exactly one distinct entrypoint was found across every candidate root.</summary>
    Resolved,

    /// <summary>More than one candidate root yielded a distinct, independently valid entrypoint.
    /// Never chosen between silently.</summary>
    Ambiguous,
}

/// <summary>
/// The result of one resolution attempt. <see cref="Target"/> is set only when
/// <see cref="Kind"/> is <see cref="PackageEntrypointResolutionKind.Resolved"/>.
/// </summary>
internal readonly record struct PackageEntrypointResolution
{
    public required PackageEntrypointResolutionKind Kind { get; init; }

    public ProviderLaunchTarget.NodeScript? Target { get; init; }

    public static readonly PackageEntrypointResolution NotFound =
        new() { Kind = PackageEntrypointResolutionKind.NotFound };

    public static readonly PackageEntrypointResolution Ambiguous =
        new() { Kind = PackageEntrypointResolutionKind.Ambiguous };

    public static PackageEntrypointResolution Resolved(ProviderLaunchTarget.NodeScript target) =>
        new() { Kind = PackageEntrypointResolutionKind.Resolved, Target = target };
}

/// <summary>
/// Resolves a provider's npm-distributed CLI to a direct <see cref="ProviderLaunchTarget.NodeScript"/>
/// without ever executing npm, npx, a package-manager shim, a shell, or <c>PATHEXT</c> expansion,
/// and without ever trusting a lexically-contained path that a reparse point could physically
/// redirect elsewhere.
///
/// <para>
/// Only fixed, catalog-owned absolute directories are ever inspected — never a filesystem
/// enumeration, never the current working directory, never a repository-controlled path. Each
/// candidate root's <c>package.json</c> is read through one bounded stream (never a size
/// pre-check followed by a separate unbounded read, which would leave a
/// check-then-read race), parsed strictly, and accepted only when its declared <c>name</c>
/// matches exactly and its <c>bin</c> entry resolves, beneath that same root, to a file that
/// actually exists. The package root, <c>package.json</c>, every directory between the root and
/// the entrypoint, and the entrypoint itself must each be free of the reparse-point attribute —
/// a junction or symbolic link anywhere on that chain fails the candidate closed, because
/// lexical containment (<see cref="Path.GetFullPath"/>) proves nothing about where the file
/// physically resolves once a reparse point is involved.
/// </para>
///
/// <para>
/// Anything else — missing manifest, oversized or size-changing manifest, malformed JSON, wrong
/// package identity, missing or ambiguous <c>bin</c>, a relative bin path that is absolute or
/// that escapes the package root, or a bin target that does not exist — simply disqualifies that
/// one candidate; it is never treated as a distinct exception to report, and never partially
/// trusted.
/// </para>
/// </summary>
internal static class PackageEntrypointResolver
{
    private const int MaxPackageJsonBytes = 64 * 1024;

    public static PackageEntrypointResolution Resolve(
        PackageEntrypointDescriptor descriptor,
        IReadOnlyList<string> nodeCandidateExecutableNames,
        IReadOnlyList<string> nodeFixedHostRoots)
    {
        var distinctScriptPaths = new List<string>();

        foreach (var packageRoot in descriptor.PackageRootCandidates)
        {
            var scriptPath = TryResolveEntrypoint(packageRoot, descriptor.ExpectedPackageName, descriptor.ExpectedCommandName);
            if (scriptPath is null)
            {
                continue;
            }

            if (!distinctScriptPaths.Any(existing => string.Equals(existing, scriptPath, StringComparison.OrdinalIgnoreCase)))
            {
                distinctScriptPaths.Add(scriptPath);
            }
        }

        if (distinctScriptPaths.Count == 0)
        {
            return PackageEntrypointResolution.NotFound;
        }

        if (distinctScriptPaths.Count > 1)
        {
            return PackageEntrypointResolution.Ambiguous;
        }

        // Deliberately never the general, PATH-searching HostExecutableResolver: the Node host
        // that runs a provider's script comes only from a fixed, catalog-owned installation root
        // — never this process's ambient PATH, which a repository-controlled directory could
        // otherwise influence.
        var nodeExecutablePath = ProviderNodeHostResolver.TryResolve(nodeCandidateExecutableNames, nodeFixedHostRoots);
        if (nodeExecutablePath is null)
        {
            return PackageEntrypointResolution.NotFound;
        }

        return PackageEntrypointResolution.Resolved(new ProviderLaunchTarget.NodeScript(nodeExecutablePath, distinctScriptPaths[0]));
    }

    private static string? TryResolveEntrypoint(string packageRoot, string expectedPackageName, string expectedCommandName)
    {
        if (!Path.IsPathFullyQualified(packageRoot))
        {
            return null;
        }

        string canonicalRoot;
        try
        {
            canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageRoot));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var manifestPath = Path.Combine(canonicalRoot, "package.json");
        if (PathOrAnyAncestorHasReparsePoint(canonicalRoot, manifestPath))
        {
            return null;
        }

        var manifestJson = TryReadManifestBounded(manifestPath);
        if (manifestJson is null)
        {
            return null;
        }

        var relativeBinPath = TryReadExpectedBinEntry(manifestJson, expectedPackageName, expectedCommandName);
        if (relativeBinPath is null)
        {
            return null;
        }

        return CanonicalizeScriptWithinRoot(canonicalRoot, relativeBinPath);
    }

    /// <summary>
    /// Reads at most <see cref="MaxPackageJsonBytes"/> + 1 bytes through one open stream and
    /// rejects anything that fills that buffer — the bound is enforced by what is actually read,
    /// never by a separate <c>FileInfo.Length</c> check a concurrent replace/grow could outrun.
    /// </summary>
    private static string? TryReadManifestBounded(string manifestPath)
    {
        try
        {
            using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[MaxPackageJsonBytes + 1];
            var totalRead = 0;
            int bytesRead;
            while (totalRead < buffer.Length && (bytesRead = stream.Read(buffer, totalRead, buffer.Length - totalRead)) > 0)
            {
                totalRead += bytesRead;
            }

            return totalRead > MaxPackageJsonBytes ? null : Encoding.UTF8.GetString(buffer, 0, totalRead);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryReadExpectedBinEntry(string manifestJson, string expectedPackageName, string expectedCommandName)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                !string.Equals(nameElement.GetString(), expectedPackageName, StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("bin", out var binElement))
            {
                return null;
            }

            return binElement.ValueKind switch
            {
                // A string-form "bin" makes the package's own unscoped name the sole command;
                // it is only unambiguous when that name is exactly the command this provider
                // expects.
                JsonValueKind.String when IsExpectedUnscopedName(expectedPackageName, expectedCommandName) =>
                    NonEmptyOrNull(binElement.GetString()),
                JsonValueKind.Object when binElement.TryGetProperty(expectedCommandName, out var mapped) &&
                                           mapped.ValueKind == JsonValueKind.String =>
                    NonEmptyOrNull(mapped.GetString()),
                _ => null,
            };
        }
    }

    private static bool IsExpectedUnscopedName(string expectedPackageName, string expectedCommandName)
    {
        var separatorIndex = expectedPackageName.LastIndexOf('/');
        var unscopedName = separatorIndex >= 0 ? expectedPackageName[(separatorIndex + 1)..] : expectedPackageName;
        return string.Equals(unscopedName, expectedCommandName, StringComparison.Ordinal);
    }

    private static string? NonEmptyOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? CanonicalizeScriptWithinRoot(string canonicalRoot, string relativeBinPath)
    {
        // npm "bin" entries are always package-relative; an absolute entry is never valid and is
        // never combined with the root.
        if (Path.IsPathRooted(relativeBinPath))
        {
            return null;
        }

        string canonicalScriptPath;
        try
        {
            canonicalScriptPath = Path.GetFullPath(Path.Combine(canonicalRoot, relativeBinPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var rootWithSeparator = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;

        if (!canonicalScriptPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            // Covers both traversal that escapes the root and a bin entry equal to the root
            // itself (which can never be the file this resolves to).
            return null;
        }

        // Lexical containment proves nothing once a reparse point sits between the root and the
        // file: a junction or symbolic link anywhere on that chain can make the OS physically
        // open a completely different location than this string implies.
        if (PathOrAnyAncestorHasReparsePoint(canonicalRoot, canonicalScriptPath))
        {
            return null;
        }

        return File.Exists(canonicalScriptPath) ? canonicalScriptPath : null;
    }

    /// <summary>
    /// Walks from <paramref name="leafPath"/> up to and including <paramref name="root"/>,
    /// rejecting if any segment on that chain — the leaf file, an intermediate directory, or the
    /// root itself — carries the reparse-point attribute. Fails closed (treats as a reparse
    /// point) if a segment cannot be inspected, or if the walk somehow reaches a filesystem root
    /// without ever matching <paramref name="root"/> exactly — both should be unreachable given
    /// the caller's own prefix-containment check, but neither is trusted to be.
    /// </summary>
    private static bool PathOrAnyAncestorHasReparsePoint(string root, string leafPath)
    {
        var current = leafPath;
        while (true)
        {
            if (HasReparsePointAttribute(current))
            {
                return true;
            }

            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = parent;
        }
    }

    private static bool HasReparsePointAttribute(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
