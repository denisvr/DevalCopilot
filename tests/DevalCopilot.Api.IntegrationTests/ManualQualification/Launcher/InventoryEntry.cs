namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>One listed kind of process: a closed lowercase name, the most starts one authorization may make of it, and the exact
/// shape test. A request that matches no entry is unlisted and is refused before any process exists.</summary>
public sealed record InventoryEntry(string Name, int Limit, Func<ProcessShape, bool> Matches);
