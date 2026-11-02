namespace DevalCopilot.Api.Startup;

/// <summary>ADR-0072: why a startup recovery step was fatal.</summary>
public enum StartupFailureReason
{
    /// <summary>The database or the application data directory could not be opened, written or reached (access, space, locking,
    /// input/output).</summary>
    StorageUnavailable = 0,

    /// <summary>A safety-critical reconciliation returned a failure, an incoherent payload, or the stored state could not be read
    /// coherently (for example a schema, query, constraint or corruption error).</summary>
    StateIncoherent = 1,

    /// <summary>Any other exception; never classified as anything weaker.</summary>
    Unclassified = 2,
}
