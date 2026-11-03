namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Thrown by an environment factory that refuses to compose the session before anything exists (for example an approved
/// launch set that is missing, malformed or no longer exactly the approved files). The entry reports the closed code and starts
/// nothing: no host, directory, ledger entry or process.</summary>
public sealed class EnvironmentRefusedException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
