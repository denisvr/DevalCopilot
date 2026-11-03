namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The identity of the one Claude executable both observations used, verified against the owner's pinned SHA-256 before any
/// start. The digest and size are file facts, not a path.</summary>
public sealed record ClaudeBinaryFacts(bool Verified, long SizeBytes, string Sha256);
