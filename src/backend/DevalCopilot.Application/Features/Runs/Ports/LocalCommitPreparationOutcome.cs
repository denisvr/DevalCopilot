namespace DevalCopilot.Application.Features.Runs.Ports;

/// <summary>Why a preparation may not proceed. Every refusal is fixed, path-free and writes nothing durable.</summary>
public enum LocalCommitPreparationOutcome
{
    Prepared,
    GitUnavailable,
    GitFailed,
    HostUnsupported,
    TooManyPaths,
    SourceTooLarge,
    TotalTooLarge,
    UnsupportedPath,
    UnsupportedChange,
    UnsafeSource,
    OwnershipNotProven,
    RepositoryNotClean,
    ParentMismatch,
    ConfigurationUnsupported,
    ConversionRefused,
    AttributeSourceUnrepresented,
    IdentityUnavailable,
    CheckpointNotCurrent,
    SourceChanged,
    HooksDirectoryNotEmpty,
}
