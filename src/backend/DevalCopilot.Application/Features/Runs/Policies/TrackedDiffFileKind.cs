namespace DevalCopilot.Application.Features.Runs.Policies;

internal enum TrackedDiffFileKind
{
    /// <summary>Header plus one or more complete text hunks.</summary>
    Text,

    /// <summary>Header lines only (a mode change, or an empty file added or removed).</summary>
    MetadataOnly,

    /// <summary>A binary patch; its payload is never sent.</summary>
    Binary,

    /// <summary>A block whose format cannot be delimited without guessing.</summary>
    Unsupported,
}
