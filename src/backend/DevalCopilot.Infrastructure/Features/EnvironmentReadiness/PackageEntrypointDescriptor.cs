namespace DevalCopilot.Infrastructure.Features.EnvironmentReadiness;

/// <summary>
/// Everything <see cref="PackageEntrypointResolver"/> needs to safely resolve one capability's
/// npm-package launch entrypoint. Every value is fixed and catalog-owned — never derived from
/// project content, user input, or a filesystem enumeration.
/// </summary>
internal sealed record PackageEntrypointDescriptor
{
    /// <summary>Fixed, absolute candidate package-root directories, each checked independently.
    /// More than one candidate resolving a distinct entrypoint is ambiguity, not a choice to make
    /// silently.</summary>
    public required IReadOnlyList<string> PackageRootCandidates { get; init; }

    /// <summary>The exact <c>package.json</c> <c>"name"</c> value required at a candidate root.
    /// A manifest present at a candidate path but naming a different package is not this
    /// provider's package and is ignored, not misattributed.</summary>
    public required string ExpectedPackageName { get; init; }

    /// <summary>The exact <c>bin</c> command name expected for this provider (for example
    /// <c>"codex"</c>). Used to select the one relevant entry out of an object-form <c>bin</c>
    /// map, and to require an unambiguous match for a string-form <c>bin</c>.</summary>
    public required string ExpectedCommandName { get; init; }
}
