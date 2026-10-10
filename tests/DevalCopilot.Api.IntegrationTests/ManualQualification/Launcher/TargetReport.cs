namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The safe verdict for one provider: a closed code, the launch shape, and the version only when it was observed and is
/// a plain version token. Never a path.</summary>
public sealed record TargetReport(string Provider, bool Real, string Code, string? LaunchKind, string? Version);
