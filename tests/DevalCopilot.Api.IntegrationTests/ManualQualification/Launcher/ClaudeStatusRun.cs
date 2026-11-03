namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The closed result of the two serial observations: each observation, the table's decision, the stop code when the sequence
/// stopped before a decision and the derived configuration-directory comparison.</summary>
public sealed record ClaudeStatusRun(
    ClaudeStatusObservation Ordinary,
    ClaudeStatusObservation Filtered,
    ClaudeStatusDecision Decision,
    string? StopCode,
    ConfigDirectoryEquality ConfigDirectory);
