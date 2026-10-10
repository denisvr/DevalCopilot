namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>What registration and preparation produced: the owned run, and the one new branch reference the host was asked to create.</summary>
public sealed record SetupFacts(Guid ProjectId, Guid RunId, string WorkspaceBranchReference);
