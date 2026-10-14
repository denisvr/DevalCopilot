using System.Collections.Concurrent;
using System.Reflection;
using DevalCopilot.Application.Features.EnvironmentReadiness.Queries.GetGitHubStoredAuthentication;
using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Projects.Queries.GetProjectGitHubOrigin;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubBranchObservation;
using DevalCopilot.Application.Features.Runs.Queries.GetGitHubCiObservation;
using DevalCopilot.Infrastructure.Features.Projects;
using DevalCopilot.Infrastructure.Features.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Composition of an observation host over a sealed Git scene. No supervisor runs (the host is started without one), and every agent
/// provider adapter, every other GitHub observer and every Git writer (the local-commit preparer, the repository's mutating members and
/// worktree creation) refuses any call and records it. Reads the production startup recovery and the observation legitimately make
/// (inspecting a commit, resolving a worktree head or registration) still reach the real adapters.
/// </summary>
internal static class SealedSceneGuard
{
    private static readonly Type[] RefusedPorts =
    [
        typeof(ICodexPlanningAdapter),
        typeof(ICriticalReviewAdapter),
        typeof(ICodexChallengeResolutionAdapter),
        typeof(ICodexImplementationReviewAdapter),
        typeof(ICodexVerificationDiagnosisAdapter),
        typeof(IClaudeImplementationAdapter),
        typeof(IClaudeReviewCorrectionAdapter),
        typeof(ISimulatedAgentAdapter),
        typeof(IAccountUsageObserver),
        typeof(IGitHubStoredAuthenticationObserver),
        typeof(IGitHubOriginObserver),
        typeof(IGitHubBranchObserver),
        typeof(IGitHubCiObserver),
        typeof(ILocalCommitPreparer),
    ];

    /// <summary>Every port refused outright, plus the two ports whose mutating members alone are refused.</summary>
    public static IReadOnlyList<Type> GuardedPorts { get; } =
        [.. RefusedPorts, typeof(ILocalCommitRepository), typeof(IGitWorktreeAdapter)];

    public static void Apply(IServiceCollection services, ConcurrentQueue<string> refusals)
    {
        foreach (var port in RefusedPorts)
        {
            services.RemoveAll(port);
            services.AddSingleton(port, _ => RefusingProxy.Wrap(port, refusals, null, []));
        }

        services.RemoveAll<ILocalCommitRepository>();
        services.AddSingleton<ILocalCommitRepository>(provider => (ILocalCommitRepository)RefusingProxy.Wrap(
            typeof(ILocalCommitRepository),
            refusals,
            provider.GetRequiredService<LocalCommitGit>(),
            [nameof(ILocalCommitRepository.InspectAsync)]));
        services.RemoveAll<IGitWorktreeAdapter>();
        services.AddSingleton<IGitWorktreeAdapter>(provider => (IGitWorktreeAdapter)RefusingProxy.Wrap(
            typeof(IGitWorktreeAdapter),
            refusals,
            ActivatorUtilities.CreateInstance<GitWorktreeAdapter>(provider),
            [
                nameof(IGitWorktreeAdapter.ResolveAdministrativeDirectoryAsync),
                nameof(IGitWorktreeAdapter.GetHeadCommitShaAsync),
                nameof(IGitWorktreeAdapter.IsRegisteredAsync),
            ]));
    }

    /// <summary>Answers only the allowed members from the real adapter; every other member is recorded and refused.</summary>
    internal class RefusingProxy : DispatchProxy
    {
        private ConcurrentQueue<string>? _refusals;
        private object? _inner;
        private HashSet<string> _allowed = [];
        private string _port = string.Empty;

        public static object Wrap(Type port, ConcurrentQueue<string> refusals, object? inner, IEnumerable<string> allowed)
        {
            var proxy = (RefusingProxy)Create(port, typeof(RefusingProxy));
            proxy._refusals = refusals;
            proxy._inner = inner;
            proxy._allowed = [.. allowed];
            proxy._port = port.Name;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (_inner is not null && _allowed.Contains(name))
            {
                try
                {
                    return targetMethod.Invoke(_inner, args);
                }
                catch (TargetInvocationException exception) when (exception.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                    throw;
                }
            }

            _refusals!.Enqueue($"{_port}.{name}");
            throw new InvalidOperationException($"{_port}.{name} is refused on a sealed scene.");
        }
    }
}
