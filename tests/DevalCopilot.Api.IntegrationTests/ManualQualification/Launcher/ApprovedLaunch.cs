namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The owner-approved launch tuple of one capability: the executable and, for a Node-hosted script launch, the script that
/// is its first argument. A tuple is the only thing a version probe or a role invocation of that capability may launch.</summary>
public sealed record ApprovedLaunch(string Capability, ApprovedFile Executable, ApprovedFile? Script);
