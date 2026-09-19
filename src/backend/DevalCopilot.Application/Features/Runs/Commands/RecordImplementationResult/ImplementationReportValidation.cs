namespace DevalCopilot.Application.Features.Runs.Commands.RecordImplementationResult;

/// <summary>
/// Independently re-validates a <see cref="ValidatedImplementationReport"/> that
/// <see cref="RecordImplementationResultCommandHandler"/> did not itself construct — its public
/// factory means a caller could hand the handler one built by hand, not only one that survived
/// <see cref="ImplementationResponseParser"/>. The handler never relies solely on the parser
/// having already run; every field checked here uses the exact same shared
/// <see cref="ImplementationEvidenceValidation"/> and <see cref="Domain.Features.Runs.CollaborationMessageContentPolicy"/>
/// calls the parser itself uses, so the two can never independently drift.
/// </summary>
internal static class ImplementationReportValidation
{
    public static bool IsValid(ValidatedImplementationReport report)
    {
        if (!Domain.Features.Runs.CollaborationMessageContentPolicy.IsSafeSummary(report.Summary))
        {
            return false;
        }

        if (!ImplementationEvidenceValidation.AreValidChangedRelativePaths(
                report.ChangedRelativePaths, ImplementationReportOutputSchema.MaximumChangedPaths))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(report.ImplementationNotes) || string.IsNullOrWhiteSpace(report.RecommendedVerification))
        {
            return false;
        }

        // Unlike ImplementationNotes/RecommendedVerification, an empty string is a valid,
        // meaningful answer for these two fields ("nothing unexpected", "no remaining risk") —
        // only a null reference (never produced by the parser, but not ruled out for a
        // hand-built value) or an over-length value fails closed.
        if (report.UnexpectedDiscoveries is null || report.UnexpectedDiscoveries.Length > ImplementationReportOutputSchema.MaximumFieldLength)
        {
            return false;
        }

        if (report.RemainingRisks is null || report.RemainingRisks.Length > ImplementationReportOutputSchema.MaximumFieldLength)
        {
            return false;
        }

        return ImplementationEvidenceValidation.IsValidExecutionReportContent(report.ImplementationNotes, report.RecommendedVerification);
    }
}
