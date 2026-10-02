namespace DevalCopilot.Application.Features.Runs.Queries.GetVerificationDiagnosisStatus;

/// <summary>One pinned verification execution of a diagnosis, in claimed order: its one-based position, the command name the
/// execution snapshotted, and the execution's number, status, and exit code. Never an executable path, argument, storage
/// path, or hash.</summary>
public sealed record VerificationDiagnosisMember(
    int Position, string CommandName, int ExecutionNumber, string Status, int? ExitCode);
