namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Observes the exact approved Git once. The real observer starts it; offline tests substitute doubles. It never retries and
/// never falls back to another file or another request.</summary>
public interface IGitVersionObserver
{
    /// <summary>The names of the environment variables the observation passes to the child (never their values).</summary>
    IReadOnlyList<string> EnvironmentNames { get; }

    GitVersionObservation Observe(string gitPath, string workingDirectory, TimeSpan bound);
}
