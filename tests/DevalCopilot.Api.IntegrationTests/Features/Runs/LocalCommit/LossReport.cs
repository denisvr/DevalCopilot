namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>What the scripted repository observed when the armed loss point was reached. <see cref="Satisfied"/> is true only for a
/// before-effect point, or for an after-effect point whose REAL effect returned <c>Promoted</c>; any other real outcome is reported
/// with a bounded <see cref="Detail"/> and is never treated as a promoted state. <see cref="OutstandingEffects"/> is the number of
/// real effects still in flight at that instant and must be zero.</summary>
internal sealed record LossReport(LossPoint Point, bool Satisfied, string Detail, int OutstandingEffects);
