namespace DevalCopilot.Domain.Features.Runs;

/// <summary>The one operation family a linked local execution step names (ADR-0065): an Agent attempt (the five Agent step kinds) or a
/// verification execution (the verification step). Exactly one family identity is ever set on a dispatch witness.</summary>
public enum LocalExecutionOperationFamily
{
    Agent = 0,
    Verification = 1,
}
