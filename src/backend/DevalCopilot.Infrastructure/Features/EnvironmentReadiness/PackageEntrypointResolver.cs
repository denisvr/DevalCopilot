using System.Buffers.Binary;
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

    /// <summary>Either shape <see cref="ProviderLaunchTarget"/> supports: a package's own
    /// "bin" entry can be a JavaScript entrypoint run through a Node host, or — for a package
    /// that ships a compiled native binary directly — the executable itself.</summary>
    public ProviderLaunchTarget? Target { get; init; }

    public static readonly PackageEntrypointResolution NotFound =
        new() { Kind = PackageEntrypointResolutionKind.NotFound };

    public static readonly PackageEntrypointResolution Ambiguous =
        new() { Kind = PackageEntrypointResolutionKind.Ambiguous };

    public static PackageEntrypointResolution Resolved(ProviderLaunchTarget target) =>
        new() { Kind = PackageEntrypointResolutionKind.Resolved, Target = target };
}

/// <summary>
/// Resolves a provider's npm-distributed CLI to a <see cref="ProviderLaunchTarget"/> — either a
/// <see cref="ProviderLaunchTarget.NodeScript"/> run through a fixed, catalog-owned Node host, or,
/// for a package whose <c>bin</c> entry is itself a genuine native Windows executable (verified by
/// a bounded PE-header check, never by file extension alone), a
/// <see cref="ProviderLaunchTarget.DirectExecutable"/> — without ever executing npm, npx, a
/// package-manager shim, a shell, or <c>PATHEXT</c> expansion, and without ever trusting a
/// lexically-contained path that a reparse point could physically redirect elsewhere.
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

        var resolvedPath = distinctScriptPaths[0];

        // A package's own "bin" entry is not always a JavaScript entrypoint: a package that
        // ships a compiled native binary directly (observed for real in
        // "@anthropic-ai/claude-code", whose postinstall step replaces "bin/claude.exe" with an
        // actual platform-specific native executable, never a script) must be launched
        // directly — running it *through* a Node host would try to parse a PE binary as
        // JavaScript source and fail. Extension alone is never trusted, and neither is the bare
        // two-byte "MZ" signature alone — that is only the DOS-stub magic number, present in any
        // DOS-executable-shaped file, and proves nothing about the PE header a genuine Windows
        // executable also carries. A bounded structural check (MZ, then a sane e_lfanew pointer,
        // then the "PE\0\0" signature it points to) is performed before this is ever treated as
        // directly executable, so a ".exe"-suffixed file that is not actually a real PE image
        // fails closed to NotFound, never silently re-interpreted as a script.
        if (resolvedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return LooksLikeNativeWindowsExecutable(resolvedPath)
                ? PackageEntrypointResolution.Resolved(new ProviderLaunchTarget.DirectExecutable(resolvedPath))
                : PackageEntrypointResolution.NotFound;
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

        return PackageEntrypointResolution.Resolved(new ProviderLaunchTarget.NodeScript(nodeExecutablePath, resolvedPath));
    }

    /// <summary>A bounded, real DOS+PE header check — never trusts a ".exe" extension, and never
    /// trusts the bare two-byte "MZ" signature alone, since that magic number is only the DOS
    /// stub's own marker and says nothing about whether a PE header actually follows it (any file
    /// starting with those two bytes would otherwise pass). Three structural facts are verified,
    /// each with a bounded read, before this is ever treated as a genuine native Windows
    /// executable: (1) the "MZ" signature at offset 0; (2) a sane <c>e_lfanew</c> pointer at the
    /// fixed DOS-header offset 0x3C — it must land at or past the end of the DOS header itself and
    /// within a small fixed bound, and must not point past the end of the file; (3) the literal
    /// "PE\0\0" signature at the offset <c>e_lfanew</c> names. Fails closed (<see langword="false"/>)
    /// on a short file, a malformed or out-of-range <c>e_lfanew</c>, a missing "PE\0\0" signature,
    /// or any read failure.</summary>
    private static bool LooksLikeNativeWindowsExecutable(string path)
    {
        // The DOS header is exactly 64 (0x40) bytes, with e_lfanew as its last field — no
        // well-formed PE image ever points its PE header inside that range. The upper bound is
        // not a format requirement, only a defensive cap: a real toolchain's DOS stub is always a
        // few hundred bytes at most, so a value this large is itself a sign of a hostile or
        // corrupt file, not a legitimate one this bounded read should keep chasing.
        const int MinPeHeaderOffset = 0x40;
        const int MaxPeHeaderOffset = 64 * 1024;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            Span<byte> dosSignature = stackalloc byte[2];
            if (stream.Read(dosSignature) != 2 || dosSignature[0] != (byte)'M' || dosSignature[1] != (byte)'Z')
            {
                return false;
            }

            if (stream.Length < MinPeHeaderOffset)
            {
                return false;
            }

            stream.Position = 0x3C;
            Span<byte> peHeaderOffsetBytes = stackalloc byte[4];
            if (stream.Read(peHeaderOffsetBytes) != 4)
            {
                return false;
            }

            var peHeaderOffset = BinaryPrimitives.ReadInt32LittleEndian(peHeaderOffsetBytes);
            if (peHeaderOffset < MinPeHeaderOffset || peHeaderOffset > MaxPeHeaderOffset || peHeaderOffset > stream.Length - 4)
            {
                return false;
            }

            stream.Position = peHeaderOffset;
            Span<byte> peSignature = stackalloc byte[4];
            return stream.Read(peSignature) == 4
                && peSignature[0] == (byte)'P' && peSignature[1] == (byte)'E' && peSignature[2] == 0 && peSignature[3] == 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
