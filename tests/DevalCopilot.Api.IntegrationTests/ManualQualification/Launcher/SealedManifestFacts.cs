namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The only things the session takes from a sealed manifest: whether its declared contract and objective are the expected
/// ones, and the identity of the proposal a review manifest names. The manifest text itself is never kept.</summary>
public sealed record SealedManifestFacts(bool ContractAgrees, bool ObjectiveAgrees, Guid? ProposalMessageId)
{
    public static SealedManifestFacts None { get; } = new(false, false, null);
}
