namespace DevalCopilot.Api.Features.Runs.GetVerificationDiagnosisStatus;

/// <summary>One pinned verification execution, in claimed order. Never an executable path, argument, storage path, or hash.</summary>
public sealed record VerificationDiagnosisMemberResponse(int Position, string CommandName, int ExecutionNumber, string Status, int? ExitCode);
