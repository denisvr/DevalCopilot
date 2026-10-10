namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public enum SubmissionOutcome
{
    /// <summary>The host answered with a durable attempt: a provider invocation may follow.</summary>
    Accepted,

    /// <summary>A definite client-error answer: the host refused before creating an attempt.</summary>
    Refused,

    /// <summary>A timeout, a transport failure, a server error or an unreadable answer: the host may have accepted it.</summary>
    Unknown,
}
