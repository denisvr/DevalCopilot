namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

public sealed record TargetJudgement(bool Accepted, IReadOnlyList<TargetReport> Targets)
{
    public string Code => Accepted
        ? "RealTargets"
        : string.Join(",", Targets.Where(target => !target.Real).Select(target => $"{target.Provider}.{target.Code}"));
}
