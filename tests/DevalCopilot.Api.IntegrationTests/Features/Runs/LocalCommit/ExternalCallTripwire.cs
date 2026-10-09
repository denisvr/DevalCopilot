using System.Collections.Concurrent;
using System.Reflection;

namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>
/// Forwards to the real adapter and records every call made once armed, so a test can prove a read reached no Git adapter at all.
/// Arming happens after host startup because the production startup recovery legitimately inspects operations.
/// </summary>
internal sealed class ExternalCallTripwire
{
    private int armed;

    public ConcurrentQueue<string> Calls { get; } = new();

    public void Arm() => Interlocked.Exchange(ref armed, 1);

    public bool IsArmed => Volatile.Read(ref armed) == 1;

    public T Wrap<T>(T inner) where T : class
    {
        var proxy = DispatchProxy.Create<T, TripwireProxy<T>>();
        var tripwire = (TripwireProxy<T>)(object)proxy;
        tripwire.Configure(this, inner);
        return proxy;
    }

    internal class TripwireProxy<T> : DispatchProxy where T : class
    {
        private ExternalCallTripwire? owner;
        private T? inner;

        public void Configure(ExternalCallTripwire tripwire, T target)
        {
            owner = tripwire;
            inner = target;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (owner!.IsArmed)
            {
                owner.Calls.Enqueue($"{typeof(T).Name}.{targetMethod!.Name}");
            }

            try
            {
                return targetMethod!.Invoke(inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
