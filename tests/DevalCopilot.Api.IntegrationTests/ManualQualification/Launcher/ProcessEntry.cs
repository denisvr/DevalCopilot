namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>One process as the table reports it. A process is identified by its id together with its creation time, so a reused id
/// is never mistaken for the process that held it earlier; an unknown creation time is an identity that cannot be confirmed.</summary>
public sealed record ProcessEntry(int Id, int ParentId, string ExecutableName, DateTime? StartedUtc = null);
