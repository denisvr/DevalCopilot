namespace DevalCopilot.Api.IntegrationTests.Features.Runs.LocalCommit;

/// <summary>The two sealed images of the one production lineage a class-owned snapshot keeps, in the order the lineage reached them.</summary>
internal enum GitHubCheckContextsSealedStage
{
    /// <summary>The delivery completed and the ADR-0040 target recorded; the ADR-0043 binding is not yet recorded.</summary>
    TargetRecorded,

    /// <summary>The same lineage after the production identity-binding POST recorded the binding and its event.</summary>
    BindingRecorded,
}
