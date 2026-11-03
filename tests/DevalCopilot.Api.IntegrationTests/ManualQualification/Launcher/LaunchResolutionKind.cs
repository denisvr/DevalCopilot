namespace DevalCopilot.Api.IntegrationTests.ManualQualification;

/// <summary>The closed outcome of the production target resolver for one capability, as the launcher reads it.</summary>
public enum LaunchResolutionKind
{
    /// <summary>A direct native executable and nothing else.</summary>
    Direct,

    /// <summary>A Node executable that runs a script, which is its first argument.</summary>
    NodeScript,

    /// <summary>The resolver found nothing.</summary>
    NotFound,

    /// <summary>The resolver found more than one distinct target and chose none.</summary>
    Ambiguous,

    /// <summary>The resolver could not be read or answered with a shape the launcher does not recognise.</summary>
    Unavailable,
}
