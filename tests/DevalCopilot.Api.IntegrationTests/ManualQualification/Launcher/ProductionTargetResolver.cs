using System.Reflection;
using DevalCopilot.Domain.Features.EnvironmentReadiness;
using DevalCopilot.Infrastructure.Features.Processes;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The launcher's one narrow binding to the production target resolver: the exact private resolution step that
/// <c>ToolDiscoveryAdapter.DiscoverAsync</c> runs before it probes, called with the catalog's own descriptor for the capability, in
/// this process's own environment. It runs only the resolution (a search of the host search path, the fixed fallback directories and
/// the bounded npm package entrypoint) and never the probe: no process, no scratch directory, no shell. The resolver is internal to
/// Infrastructure and its source stays outside this lot, so the binding is by reflection, and every part of it is checked against
/// the exact expected shape first (type names, parameter types, result members and target types). Any difference, or any failure of a
/// call, is <see cref="LaunchResolutionKind.Unavailable"/> and refuses the launch; nothing falls back to a copy of the resolver's
/// rules, and no path or environment value is reported.
/// </summary>
public static class ProductionTargetResolver
{
    private const string Namespace = "DevalCopilot.Infrastructure.Features.EnvironmentReadiness";
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>The binding, or null when the production resolver no longer has exactly the shape this launcher reviewed.</summary>
    public static Func<Capability, LaunchResolution>? Bind()
    {
        try
        {
            var assembly = typeof(ChildProcessExecutionAdapter).Assembly;
            var adapter = assembly.GetType($"{Namespace}.ToolDiscoveryAdapter", throwOnError: false);
            var catalog = assembly.GetType($"{Namespace}.HostCapabilityCatalog", throwOnError: false);
            var descriptor = assembly.GetType($"{Namespace}.CapabilityProbeDescriptor", throwOnError: false);
            var direct = assembly.GetType($"{Namespace}.ProviderLaunchTarget+DirectExecutable", throwOnError: false);
            var script = assembly.GetType($"{Namespace}.ProviderLaunchTarget+NodeScript", throwOnError: false);
            if (adapter is null || catalog is null || descriptor is null || direct is null || script is null)
            {
                return null;
            }

            var describe = catalog.GetMethod("Get", Static, null, [typeof(Capability)], null);
            var resolve = adapter.GetMethod("ResolveLaunchTarget", Static, null, [typeof(Capability), descriptor], null);
            var result = resolve?.ReturnType;
            if (describe is null || describe.ReturnType != descriptor || resolve is null || result is null
                || result.Name != "LaunchTargetResolution" || !result.IsValueType || result.DeclaringType != adapter)
            {
                return null;
            }

            var target = result.GetProperty("Target", BindingFlags.Instance | BindingFlags.Public);
            var failure = result.GetProperty("FailureReason", BindingFlags.Instance | BindingFlags.Public);
            var directPath = direct.GetProperty("ExecutablePath", BindingFlags.Instance | BindingFlags.Public);
            var nodePath = script.GetProperty("NodeExecutablePath", BindingFlags.Instance | BindingFlags.Public);
            var scriptPath = script.GetProperty("ScriptPath", BindingFlags.Instance | BindingFlags.Public);
            if (target is null || failure is null || directPath is null || nodePath is null || scriptPath is null
                || failure.PropertyType != typeof(CapabilityProbeReason?)
                || !target.PropertyType.IsAssignableFrom(direct) || !target.PropertyType.IsAssignableFrom(script)
                || directPath.PropertyType != typeof(string) || nodePath.PropertyType != typeof(string)
                || scriptPath.PropertyType != typeof(string))
            {
                return null;
            }

            return capability => Resolve(capability, describe, resolve, target, failure, direct, script, directPath, nodePath, scriptPath);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static LaunchResolution Resolve(
        Capability capability,
        MethodInfo describe,
        MethodInfo resolve,
        PropertyInfo target,
        PropertyInfo failure,
        Type direct,
        Type script,
        PropertyInfo directPath,
        PropertyInfo nodePath,
        PropertyInfo scriptPath)
    {
        try
        {
            var resolution = resolve.Invoke(null, [capability, describe.Invoke(null, [capability])]);
            var launch = target.GetValue(resolution);
            var reason = (CapabilityProbeReason?)failure.GetValue(resolution);
            if (launch is null)
            {
                return reason switch
                {
                    CapabilityProbeReason.ExecutableNotFound => new LaunchResolution(LaunchResolutionKind.NotFound),
                    CapabilityProbeReason.LaunchTargetAmbiguous => new LaunchResolution(LaunchResolutionKind.Ambiguous),
                    _ => LaunchResolution.Unavailable,
                };
            }

            // A resolution is a launch or a failure, never both.
            if (reason is not null)
            {
                return LaunchResolution.Unavailable;
            }

            if (launch.GetType() == direct && directPath.GetValue(launch) is string executable)
            {
                return new LaunchResolution(LaunchResolutionKind.Direct, executable);
            }

            return launch.GetType() == script && nodePath.GetValue(launch) is string node && scriptPath.GetValue(launch) is string entry
                ? new LaunchResolution(LaunchResolutionKind.NodeScript, node, entry)
                : LaunchResolution.Unavailable;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return LaunchResolution.Unavailable;
        }
    }
}
