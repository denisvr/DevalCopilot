using System.ComponentModel;

namespace DevalCopilot.Infrastructure.Features.Processes;

/// <summary>
/// The one narrow, explicit environment rule for a Node runtime process launch on Windows: Node
/// aborts at startup under an otherwise-empty environment on Windows, so a request that starts
/// the Node runtime — a NodeScript launch, or the native probe of the explicit Node capability —
/// must carry the OS prerequisite <c>SystemRoot</c>. Shared by every caller that builds such a
/// request (capability discovery, the Codex invocation contract and the Codex App Server session)
/// so they cannot drift apart.
/// The value is derived only from the parent of the managed system directory
/// (<c>Environment.SystemDirectory</c>, backed by <c>GetSystemDirectoryW</c>) — never read from
/// the ambient <c>SystemRoot</c>/<c>windir</c> variables, hardcoded, searched for or taken from
/// repository or user configuration — and validated only lexically. The rule never adds
/// <c>PATH</c>, <c>NODE_OPTIONS</c>, a token, a proxy/certificate setting, a temp location or
/// any other ambient entry; a request that does not require the Node runtime, or a non-Windows
/// request, is never touched and never evaluates the system-directory reader.
/// </summary>
internal static class WindowsNodeProcessEnvironment
{
    internal const string SystemRootVariable = "SystemRoot";

    private static readonly char[] DirectorySeparators = ['\\', '/'];

    /// <summary>The two OS facts the rule depends on, injectable so a caller's real control flow is
    /// testable with controlled inputs and no mutable static state.</summary>
    internal readonly record struct Inputs(bool IsWindows, Func<string?>? ReadSystemDirectory)
    {
        internal static Inputs Current { get; } = new(OperatingSystem.IsWindows(), static () => System.Environment.SystemDirectory);
    }

    /// <summary>
    /// Adds exactly <c>SystemRoot</c> to <paramref name="environment"/> when the request is a
    /// Windows launch that <paramref name="requiresNodeRuntime"/> and returns
    /// <see langword="true"/>; returns <see langword="true"/> without evaluating the reader or
    /// touching the map for every other request. Returns <see langword="false"/>, leaving the map
    /// unmodified, when a required Windows value cannot be resolved or fails lexical validation —
    /// the caller must then refuse before any process dispatch, with no retry or fallback to a
    /// smaller map. Never carries a value or exception detail out.
    /// </summary>
    internal static bool TryAdd(IDictionary<string, string> environment, bool requiresNodeRuntime, Inputs inputs)
    {
        if (!requiresNodeRuntime || !inputs.IsWindows)
        {
            return true;
        }

        if (!TryResolveSystemRoot(inputs.ReadSystemDirectory, out var systemRoot))
        {
            return false;
        }

        environment[SystemRootVariable] = systemRoot;
        return true;
    }

    private static bool TryResolveSystemRoot(Func<string?>? readSystemDirectory, out string systemRoot)
    {
        systemRoot = string.Empty;

        string? systemDirectory;
        try
        {
            systemDirectory = readSystemDirectory?.Invoke();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(systemDirectory)
            || !IsLexicallySafe(systemDirectory)
            || !Path.IsPathFullyQualified(systemDirectory))
        {
            return false;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(systemDirectory);
        var parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrWhiteSpace(parent)
            || !Path.IsPathFullyQualified(parent)
            || IsFilesystemRoot(parent)
            || !IsParentOf(parent, trimmed))
        {
            return false;
        }

        systemRoot = parent;
        return true;
    }

    private static bool IsLexicallySafe(string path) =>
        path.IndexOfAny(Path.GetInvalidPathChars()) < 0
        && !path.Any(char.IsControl)
        && !path.Split(DirectorySeparators).Any(component => component is "." or "..");

    private static bool IsFilesystemRoot(string path) =>
        Path.GetPathRoot(path) is { } root
        && string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(path), StringComparison.Ordinal);

    /// <summary>The parent derivation must be exactly "<paramref name="parent"/> + one separator +
    /// one nonempty leaf": a repeated separator, a normalization that changed the text, or any
    /// other mismatch would make the derived value something other than the directory above the
    /// system directory.</summary>
    private static bool IsParentOf(string parent, string path)
    {
        if (!path.StartsWith(parent, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = path.AsSpan(parent.Length);
        return remainder.Length > 1
            && Array.IndexOf(DirectorySeparators, remainder[0]) >= 0
            && remainder[1..].IndexOfAny(DirectorySeparators) < 0;
    }
}
