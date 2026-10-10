using DevalCopilot.Api.IntegrationTests.BrowserJourney.Host;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// Removes exactly the one disposable root this session created, and nothing else. Ownership is re-proven immediately before the
/// first deletion (a real directory directly under the temp directory, with this session's prefix and marker token), and a root
/// that holds any alias is preserved rather than traversed. A sibling that merely shares the prefix is never touched.
/// </summary>
public static class OwnedRootCleaner
{
    private static readonly EnumerationOptions EveryEntry = new() { AttributesToSkip = 0, IgnoreInaccessible = false };

    private static readonly EnumerationOptions EveryFileBelow =
        new() { AttributesToSkip = 0, RecurseSubdirectories = true };

    public static CleanupReport Remove(string root, string token)
    {
        OwnedRootGuard guard;
        try
        {
            guard = OwnedRootGuard.Verify(root, token);
        }
        catch (InvalidOperationException)
        {
            return CleanupReport.Preserved("OwnershipUnproven");
        }

        try
        {
            if (ContainsAlias(guard.Root))
            {
                return CleanupReport.Preserved("AliasInsideRoot");
            }

            ClearReadOnly(guard.Root);
            DeleteMarkerLast(guard.Root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return CleanupReport.Preserved("DeletionFailed");
        }

        return Directory.Exists(guard.Root) ? CleanupReport.Preserved("DeletionIncomplete") : CleanupReport.RemovedRoot;
    }

    private static bool ContainsAlias(string directory) =>
        Directory.EnumerateFileSystemEntries(directory, "*", EveryEntry)
            .Any(entry => File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint))
        || Directory.EnumerateDirectories(directory, "*", EveryEntry).Any(ContainsAlias);

    private static void ClearReadOnly(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", EveryFileBelow))
        {
            if (File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
    }

    /// <summary>Everything else goes first and the ownership marker last, so a deletion that fails part way leaves a root that can
    /// still prove it is the session's own.</summary>
    private static void DeleteMarkerLast(string root)
    {
        var marker = Path.Combine(root, OwnedRootGuard.MarkerFile);
        var others = Directory.EnumerateFileSystemEntries(root, "*", EveryEntry)
            .Where(entry => !string.Equals(entry, marker, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var entry in others)
        {
            WithRetry(() => DeleteEntry(entry));
        }

        File.Delete(marker);
        Directory.Delete(root);
    }

    private static void DeleteEntry(string entry)
    {
        if (Directory.Exists(entry))
        {
            Directory.Delete(entry, recursive: true);
        }
        else
        {
            File.Delete(entry);
        }
    }

    private static void WithRetry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(500);
            }
        }
    }
}
