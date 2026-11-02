namespace DevalCopilot.Application.Features.Projects.Ports;

/// <summary>
/// ADR-0072: the prepared environment of one verification execution, or its absence. A prepared environment carries only the
/// explicit keys of the fixed profile and the profile version that is recorded with the result; it never carries ambient values.
/// </summary>
public sealed record VerificationEnvironmentPreparation(
    bool IsAvailable, string ProfileVersion, IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    public static VerificationEnvironmentPreparation Unavailable(string profileVersion) =>
        new(false, profileVersion, new Dictionary<string, string>());

    /// <summary>Never prints a value: the keys and values may contain local paths.</summary>
    public override string ToString() =>
        $"{nameof(VerificationEnvironmentPreparation)} {{ {nameof(IsAvailable)} = {IsAvailable}, {nameof(ProfileVersion)} = {ProfileVersion}, " +
        $"KeyCount = {EnvironmentVariables.Count} }}";
}
