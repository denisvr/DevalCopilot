namespace DevalCopilot.Domain.Features.Runs;

/// <summary>What an Agent attempt's own immutable record says about direct human guidance. It states what the host
/// recorded and supplied, never that a provider followed it, and a null snapshot is never read as submission history
/// (it is equally the state of an attempt claimed before this feature and of one claimed without guidance).</summary>
public enum DirectHumanGuidanceEvidence
{
    /// <summary>A coherent mutation attempt whose snapshot is null: no direct guidance was recorded. Nothing is claimed
    /// about whether any text was ever submitted.</summary>
    NotRecorded = 0,

    /// <summary>A coherent v2 mutation attempt whose immutable snapshot records the accepted guidance text.</summary>
    Provided = 1,

    /// <summary>The recorded facts do not agree (guidance beside a contract that cannot carry it, malformed text, or
    /// an unrecognized version): nothing is claimed and no text is exposed.</summary>
    Unknown = 2,
}
