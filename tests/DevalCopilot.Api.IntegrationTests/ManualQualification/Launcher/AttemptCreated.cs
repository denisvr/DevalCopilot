namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The one member of either submission answer the session needs: the identity of the durable attempt.</summary>
public sealed record AttemptCreated(Guid AttemptId);
