namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>The validated latest configuration revision (ADR-0059): its revision and its limit, one or two.</summary>
internal readonly record struct ExecutionCapacityConfiguration(long Revision, int Limit);
