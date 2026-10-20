namespace DevalCopilot.Application.Features.Runs.Policies.ExecutionCapacity;

/// <summary>The exact owner a dispatch or execution gate is itself: its kind and immutable identifier, validated as a coherent counted
/// owner of that kind before it is excluded (ADR-0058).</summary>
internal readonly record struct ExecutionOwner(ExecutionOwnerKind Kind, Guid Id);
