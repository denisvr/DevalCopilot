namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Which of the two status contexts the runner is about to start, and the one disposable working directory it starts them in,
/// so the admission inventory can tell two requests of the same shape apart and bind the destination without looking at any
/// environment value.</summary>
public sealed class StatusContext
{
    private volatile string _current = string.Empty;
    private volatile string? _workingDirectory;

    public string Current
    {
        get => _current;
        set => _current = value;
    }

    /// <summary>The entry's own working directory; null until it exists, so no start is admitted before it does.</summary>
    public string? WorkingDirectory
    {
        get => _workingDirectory;
        set => _workingDirectory = value;
    }
}
