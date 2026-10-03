namespace DevalCopilot.Api.IntegrationTests.BrowserJourney.ProviderFixture;

/// <summary>A closed refusal: the fixture accepts only its documented contracts, so anything else ends the process with a
/// fixed exit code and a fixed reason on standard error, never an improvised answer.</summary>
public sealed class FixtureRefusal(int exitCode, string reason) : Exception(reason)
{
    public const int UnsupportedInvocation = 64;
    public const int OwnershipRefused = 65;
    public const int PlanIdentityRefused = 66;

    public int ExitCode { get; } = exitCode;
}
