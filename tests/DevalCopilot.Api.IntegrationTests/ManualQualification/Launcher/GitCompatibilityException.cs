namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>A closed refusal of the launcher's Git prerequisite, raised before the first fixture mutation. The code is one of the
/// closed <c>GitVersion...</c> tokens and never carries a path, output or version text.</summary>
public sealed class GitCompatibilityException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
