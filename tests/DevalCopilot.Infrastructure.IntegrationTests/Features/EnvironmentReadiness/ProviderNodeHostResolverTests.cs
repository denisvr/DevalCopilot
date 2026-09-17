using DevalCopilot.Infrastructure.Features.EnvironmentReadiness;
using Xunit;

namespace DevalCopilot.Infrastructure.IntegrationTests.Features.EnvironmentReadiness;

/// <summary>
/// Proves <see cref="ProviderNodeHostResolver"/> never consults this process's ambient
/// <c>PATH</c> — only the fixed, catalog-owned host roots it is explicitly given. The PATH
/// variable itself is mutated for the duration of these tests (never a safe thing to do under
/// xUnit's default parallelism), so this class shares <see cref="EnvironmentPathMutationCollection"/>
/// with every other test that does the same.
/// </summary>
[Collection(EnvironmentPathMutationCollection.Name)]
public sealed class ProviderNodeHostResolverTests : IDisposable
{
    private readonly string _originalPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    private readonly string _rootDirectory =
        Path.Combine(Path.GetTempPath(), $"devalcopilot-node-host-resolver-{Guid.NewGuid():N}");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);

        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryResolve_ignores_a_node_executable_reachable_only_through_a_repository_controlled_path_directory()
    {
        // Simulates the exact attack this fix closes: a repository (or anything else that can
        // get a directory onto this process's PATH) places its own node.exe where the general,
        // PATH-searching HostExecutableResolver would have found it.
        var fakeRepositoryDirectory = Path.Combine(_rootDirectory, "fake-repository", "node_modules", ".bin");
        Directory.CreateDirectory(fakeRepositoryDirectory);
        File.WriteAllText(Path.Combine(fakeRepositoryDirectory, "node.exe"), string.Empty);
        Environment.SetEnvironmentVariable("PATH", fakeRepositoryDirectory);

        var approvedHostRoot = Path.Combine(_rootDirectory, "approved-host-root");
        Directory.CreateDirectory(approvedHostRoot);
        var approvedNodePath = Path.Combine(approvedHostRoot, "node.exe");
        File.WriteAllText(approvedNodePath, string.Empty);

        var resolved = ProviderNodeHostResolver.TryResolve(["node.exe"], [approvedHostRoot]);

        // The fixed approved root still resolves...
        Assert.Equal(approvedNodePath, resolved);
    }

    [Fact]
    public void TryResolve_returns_null_when_only_a_repository_controlled_path_directory_has_a_node_executable()
    {
        var fakeRepositoryDirectory = Path.Combine(_rootDirectory, "fake-repository", "node_modules", ".bin");
        Directory.CreateDirectory(fakeRepositoryDirectory);
        File.WriteAllText(Path.Combine(fakeRepositoryDirectory, "node.exe"), string.Empty);
        Environment.SetEnvironmentVariable("PATH", fakeRepositoryDirectory);

        var approvedHostRootWithNothingInIt = Path.Combine(_rootDirectory, "approved-host-root-empty");
        Directory.CreateDirectory(approvedHostRootWithNothingInIt);

        // ...and a fixed root that genuinely has no node.exe truthfully reports not found, rather
        // than falling back to whatever happens to be on PATH.
        var resolved = ProviderNodeHostResolver.TryResolve(["node.exe"], [approvedHostRootWithNothingInIt]);

        Assert.Null(resolved);
    }

    [Fact]
    public void TryResolve_never_finds_a_candidate_via_a_relative_fixed_root_even_if_it_exists_in_the_current_directory()
    {
        var candidateName = $"devalcopilot-cwd-node-{Guid.NewGuid():N}.exe";
        var pathInCurrentDirectory = Path.Combine(Directory.GetCurrentDirectory(), candidateName);
        File.WriteAllText(pathInCurrentDirectory, string.Empty);
        try
        {
            var resolved = ProviderNodeHostResolver.TryResolve([candidateName], ["."]);

            Assert.Null(resolved);
        }
        finally
        {
            File.Delete(pathInCurrentDirectory);
        }
    }
}
