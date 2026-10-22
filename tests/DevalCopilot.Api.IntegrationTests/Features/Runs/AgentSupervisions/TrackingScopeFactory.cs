using Microsoft.Extensions.DependencyInjection;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.AgentSupervisions;

/// <summary>
/// Test-owned completion evidence for the sweep (LCQ-V2). The production sweep intentionally lets a call that outlived its bounded stop keep
/// running in a scope it owns until the call settles. This decorator counts those scopes (created and fully disposed) so a test can prove every
/// pending call has settled before the harness deletes its database. It changes no production behavior and never waits on a delay.
/// </summary>
internal sealed class TrackingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
{
    private readonly object gate = new();
    private int active;
    private TaskCompletionSource idle = Completed();

    /// <summary>Scopes created and not yet disposed: the calls (and their database connections) still alive.</summary>
    public int Active
    {
        get
        {
            lock (gate)
            {
                return active;
            }
        }
    }

    public IServiceScope CreateScope()
    {
        var scope = inner.CreateScope();
        lock (gate)
        {
            if (active == 0)
            {
                idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            active++;
        }

        return new TrackedScope(scope, this);
    }

    /// <summary>Completes when no tracked scope is alive; fails the test after <paramref name="timeout"/> with the count still alive.</summary>
    public async Task WhenIdleAsync(TimeSpan timeout)
    {
        Task signal;
        lock (gate)
        {
            signal = idle.Task;
        }

        try
        {
            await signal.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException($"{Active} supervision call scope(s) were still alive after {timeout.TotalSeconds:F0} s.");
        }
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource();
        source.SetResult();
        return source;
    }

    private void Released()
    {
        lock (gate)
        {
            active--;
            if (active == 0)
            {
                idle.TrySetResult();
            }
        }
    }

    private sealed class TrackedScope(IServiceScope scope, TrackingScopeFactory owner) : IServiceScope, IAsyncDisposable
    {
        private int released;

        public IServiceProvider ServiceProvider => scope.ServiceProvider;

        public void Dispose()
        {
            scope.Dispose();
            Release();
        }

        public async ValueTask DisposeAsync()
        {
            if (scope is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                scope.Dispose();
            }

            Release();
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.Released();
            }
        }
    }
}
