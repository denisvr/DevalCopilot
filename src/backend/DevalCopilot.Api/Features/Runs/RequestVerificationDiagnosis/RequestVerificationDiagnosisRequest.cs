namespace DevalCopilot.Api.Features.Runs.RequestVerificationDiagnosis;

/// <summary>The one ExecutionReport whose current failed local verification is to be diagnosed. The host derives the complete
/// verification selection; nothing else is supplied.</summary>
public sealed record RequestVerificationDiagnosisRequest(Guid ExecutionReportMessageId);
