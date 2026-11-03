namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The adapter contract versions the qualification accepts, pinned at review. A stored attempt that recorded any other
/// version, including a historical one or none, can never qualify. A change of a production version fails the pinning test and
/// needs a reviewed change here.</summary>
public static class QualificationContracts
{
    public const string PlannerAdapter = "codex-planning-v1";
    public const string ReviewerAdapter = "claude-critical-review-v2";
}
