namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>Facts observed from the exact handles held by the current host before ref mutation. Persisting these facts records
/// a phase boundary; it never permits a restarted host to adopt a later pathname with the same identity or bytes.</summary>
public sealed record LocalCommitIndexAcquisition(
    string AdministrativeDirectoryIdentity,
    string PreimageIdentity,
    long PreimageLength,
    string PreparedArtifactIdentity,
    long PreparedArtifactLength,
    string LockIdentity,
    long LockLength,
    string LockName);
