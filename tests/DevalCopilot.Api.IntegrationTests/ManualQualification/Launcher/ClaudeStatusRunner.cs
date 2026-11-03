using DevalCopilot.Application.Features.Processes.Ports;

namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The two serial <c>claude auth status</c> observations through the shared bounded process boundary: direct typed arguments, no
/// shell, no terminal, no input, one disposable working directory and the same binary. The first uses the ordinary terminal's
/// environment unchanged; the second uses the production-filtered map. The sequence stops at the first observation that is not
/// coherent, never starts a second comparison, a fallback context or another command, and decides only by the plan's table.
/// </summary>
public sealed class ClaudeStatusRunner(IProcessExecutionAdapter adapter, StatusContext context)
{
    public async Task<ClaudeStatusRun> RunAsync(
        string claudeExecutablePath,
        string workingDirectory,
        IReadOnlyDictionary<string, string> ordinaryEnvironment,
        IReadOnlyDictionary<string, string>? filteredEnvironment,
        TimeSpan execution,
        TimeSpan sequence,
        CancellationToken cancellationToken)
    {
        var none = ClaudeStatusObservation.Unobserved(ClaudeStatusObservation.Ordinary, ClaudeStatusPhysical.NotStarted);
        var notStarted = ClaudeStatusObservation.Unobserved(ClaudeStatusObservation.Filtered, ClaudeStatusPhysical.NotStarted);
        if (filteredEnvironment is null)
        {
            return Stopped(none, notStarted, "FilteredEnvironmentUnavailable");
        }

        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(sequence);
        var ordinary = await ObserveAsync(
            ClaudeStatusObservation.Ordinary, claudeExecutablePath, workingDirectory, ordinaryEnvironment, execution, bound.Token);
        if (!ordinary.IsCoherent)
        {
            return Stopped(ordinary, notStarted, "OrdinaryObservationNotCoherent");
        }

        var filtered = await ObserveAsync(
            ClaudeStatusObservation.Filtered, claudeExecutablePath, workingDirectory, filteredEnvironment, execution, bound.Token);
        if (!filtered.IsCoherent)
        {
            return Stopped(ordinary, filtered, "FilteredObservationNotCoherent");
        }

        var equality = ordinary.ConfigDirectoryFingerprint is { } left && filtered.ConfigDirectoryFingerprint is { } right
            ? (left == right ? ConfigDirectoryEquality.Equal : ConfigDirectoryEquality.Different)
            : ConfigDirectoryEquality.Unknown;
        var decision = (ordinary.State, filtered.State) switch
        {
            (ClaudeStatusState.ReportedAuthenticated, ClaudeStatusState.ReportedAuthenticated) =>
                ClaudeStatusDecision.BothAuthenticated,
            (ClaudeStatusState.ReportedAuthenticated, ClaudeStatusState.ReportedLoggedOut) =>
                ClaudeStatusDecision.OrdinaryAuthenticatedFilteredLoggedOut,
            (ClaudeStatusState.ReportedLoggedOut, ClaudeStatusState.ReportedLoggedOut) => ClaudeStatusDecision.BothLoggedOut,
            _ => ClaudeStatusDecision.ReverseDiscrepancy,
        };
        return new ClaudeStatusRun(ordinary, filtered, decision, null, equality);
    }

    private static ClaudeStatusRun Stopped(ClaudeStatusObservation ordinary, ClaudeStatusObservation filtered, string code) =>
        new(ordinary, filtered, ClaudeStatusDecision.Stopped, code, ConfigDirectoryEquality.Unknown);

    private async Task<ClaudeStatusObservation> ObserveAsync(
        string name,
        string claudeExecutablePath,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan execution,
        CancellationToken cancellationToken)
    {
        context.Current = name;
        var request = new ProcessExecutionRequest
        {
            ExecutablePath = claudeExecutablePath,
            Arguments = ["auth", "status"],
            WorkingDirectory = workingDirectory,
            ApprovedRoot = workingDirectory,
            EnvironmentVariables = environment,
            Timeout = execution,
            MaxBytesPerStream = ClaudeStatusProjector.MaximumStreamBytes,
            MaxTotalCapturedBytes = 2 * ClaudeStatusProjector.MaximumStreamBytes,
        };
        try
        {
            var result = await adapter.ExecuteAsync(request, cancellationToken);
            return ClaudeStatusProjector.Project(name, result);
        }
        catch (ProcessAdmissionRefusedException)
        {
            return ClaudeStatusObservation.Unobserved(name, ClaudeStatusPhysical.Refused);
        }
        catch (OperationCanceledException)
        {
            return ClaudeStatusObservation.Unobserved(name, ClaudeStatusPhysical.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The closed outcome only: an adapter or launch exception carries a path or a message that is never reported.
            return ClaudeStatusObservation.Unobserved(name, ClaudeStatusPhysical.StartFailed);
        }
    }
}
