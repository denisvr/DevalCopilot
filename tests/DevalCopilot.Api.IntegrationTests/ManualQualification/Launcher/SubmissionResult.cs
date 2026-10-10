namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public sealed record SubmissionResult(SubmissionOutcome Outcome, Guid? AttemptId, string? Code)
{
    public static SubmissionResult Accepted(Guid attemptId) => new(SubmissionOutcome.Accepted, attemptId, null);

    public static SubmissionResult Refused(string? code) => new(SubmissionOutcome.Refused, null, code);

    public static SubmissionResult Unknown() => new(SubmissionOutcome.Unknown, null, null);
}
