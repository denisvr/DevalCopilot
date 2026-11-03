namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The closed counts of one session's process admission: starts admitted per listed class, refusals per closed code, the
/// first reason that closed the gate (null while it is open) and the classes of requests still in flight (verified or being recorded,
/// not yet admitted). Never a path, an argument or any text of a request.</summary>
public sealed record ProcessAdmissionFacts(
    IReadOnlyDictionary<string, int> Admitted,
    IReadOnlyDictionary<string, int> Refused,
    string? ClosedBy = null,
    IReadOnlyList<string>? Pending = null)
{
    public static ProcessAdmissionFacts None { get; } = new(
        new Dictionary<string, int>(),
        new Dictionary<string, int>());

    public IReadOnlyList<string> PendingClasses => Pending ?? [];

    public int TotalAdmitted => Admitted.Values.Sum();

    public int TotalRefused => Refused.Values.Sum();
}
