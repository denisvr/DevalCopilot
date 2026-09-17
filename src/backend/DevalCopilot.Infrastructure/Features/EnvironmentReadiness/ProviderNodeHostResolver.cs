namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Resolves the direct, fully qualified <c>node.exe</c> used to run a validated
/// <see cref="ProviderLaunchTarget.NodeScript"/> entrypoint. Deliberately narrower than
/// <see cref="HostExecutableResolver"/>: it never consults this process's ambient <c>PATH</c>,
/// only a fixed, catalog-owned list of absolute host installation roots. A repository-controlled
/// directory (or any other directory an attacker could get onto <c>PATH</c>) can never affect
/// which <c>node.exe</c> runs a provider's code, because <c>PATH</c> is never read here at all.
/// Never a shell, never <c>cwd</c>, never npm/npx, never a package-manager shim, never the
/// private Codex desktop application's own layout.
/// </summary>
internal static class ProviderNodeHostResolver
{
    public static string? TryResolve(IReadOnlyList<string> candidateExecutableNames, IReadOnlyList<string> fixedHostRoots)
    {
        foreach (var root in fixedHostRoots)
        {
            if (!Path.IsPathFullyQualified(root))
            {
                continue;
            }

            string canonicalRoot;
            try
            {
                canonicalRoot = Path.GetFullPath(root);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            foreach (var candidateName in candidateExecutableNames)
            {
                var candidatePath = Path.Combine(canonicalRoot, candidateName);
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }
        }

        return null;
    }
}
