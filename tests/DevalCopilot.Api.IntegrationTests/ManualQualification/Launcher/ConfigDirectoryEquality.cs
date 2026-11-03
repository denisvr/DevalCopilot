namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Whether the documented <c>configDirectory</c> field, when present in both observations, named the same directory. It is
/// derived in memory from a one-way fingerprint and never stored or printed; a missing field in either observation is
/// <see cref="Unknown"/>.</summary>
public enum ConfigDirectoryEquality
{
    Unknown = 0,
    Equal = 1,
    Different = 2,
}
