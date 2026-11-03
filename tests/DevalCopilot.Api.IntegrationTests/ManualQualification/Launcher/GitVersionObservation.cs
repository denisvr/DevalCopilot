namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// What one bounded <c>--version</c> request of the approved Git returned. <see cref="Exited"/> and <see cref="ExitCode"/> are the
/// process facts SAMPLED AT THE DECISION (before any cleanup); <see cref="TimedOut"/> says the one absolute observation deadline had
/// elapsed when the wait's result was read, or the wait itself timed out, whatever the process then did; <see cref="Truncated"/>
/// says either stream exceeded its byte cap; <see cref="IoComplete"/> and <see cref="TerminationUnproven"/> are measured AFTER the one
/// best-effort cleanup: <see cref="IoComplete"/> is true only when both streams were successfully read to their end (false when the
/// process could not be started or none was returned), and <see cref="TerminationUnproven"/> says cleanup could not prove that the
/// process (or a descendant holding a pipe) ended;
/// <see cref="Output"/> is the bounded standard output text, never kept beyond the gate and never printed. The standard error bytes
/// are only counted against their cap and discarded.
/// </summary>
public sealed record GitVersionObservation(
    bool Exited,
    int? ExitCode,
    bool TimedOut,
    bool Truncated,
    string Output,
    bool IoComplete = true,
    bool TerminationUnproven = false);
