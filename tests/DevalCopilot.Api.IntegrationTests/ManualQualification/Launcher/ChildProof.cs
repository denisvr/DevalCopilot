namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>Whether every process this launcher's host started is proven gone. Anything short of proof carries a closed reason.</summary>
public sealed record ChildProof(bool Proven, string Reason)
{
    public static ChildProof Stopped { get; } = new(true, "Proven");

    public static ChildProof Unproven(string reason) => new(false, reason);
}
