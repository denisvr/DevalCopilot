namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>
/// The exact root an environment created, with the ownership token it generated at that moment. It is held from creation, so a
/// holder never needs to discover a root or to read a token back from a directory. It is not a way to remove anything: removal
/// stays with the session's cleanup gate, and only offline tests read this reference.
/// </summary>
public sealed class OwnedRootReference(string path, string ownerToken)
{
    public string Path { get; } = path;

    public string OwnerToken { get; } = ownerToken;

    public override string ToString() => nameof(OwnedRootReference);
}
