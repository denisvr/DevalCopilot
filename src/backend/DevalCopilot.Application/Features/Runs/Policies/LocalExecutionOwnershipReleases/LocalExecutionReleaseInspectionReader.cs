using DevalCopilot.Application.Data;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Runs.Policies.LocalExecutionOwnershipReleases;

/// <summary>
/// Reads the recorded release receipt of one step of one run (ADR-0067) with exactly one database statement, the release statement, and
/// proves it with the release proof without changing either. The proof orders eligibility before a stored receipt for a step that names
/// no operation, so absence is decided here from the same rows: NotRecorded needs a supported proof result (Available or NotEligible)
/// and neither the step-keyed nor the witness-keyed receipt association; every present receipt that is not fully proved is Unavailable,
/// and so is every other result. It writes nothing, mints no confirmation and swallows no fault.
/// </summary>
internal static class LocalExecutionReleaseInspectionReader
{
    public static async Task<LocalExecutionReleaseInspectionReading> ReadAsync(
        IDevalCopilotDbContext dbContext, Guid runId, Guid stepId, CancellationToken cancellationToken)
    {
        var rows = await LocalExecutionReleaseStatement.For(dbContext, runId, stepId).ToArrayAsync(cancellationToken);
        return Classify(rows, LocalExecutionReleaseProof.Prove(rows));
    }

    public static LocalExecutionReleaseInspectionReading Classify(
        IReadOnlyList<LocalExecutionReleaseRow> rows, LocalExecutionReleaseReading proved)
    {
        switch (proved.Kind)
        {
            case LocalExecutionReleaseReadingKind.NotFound:
                return LocalExecutionReleaseInspectionReading.NotFound;
            case LocalExecutionReleaseReadingKind.Released:
                return new LocalExecutionReleaseInspectionReading(
                    LocalExecutionReleaseInspectionReadingKind.Recorded, proved.Facts, proved.Recorded);
            case LocalExecutionReleaseReadingKind.Available or LocalExecutionReleaseReadingKind.NotEligible
                when rows.All(row => row.Receipt.Id is null && row.Receipt.WitnessKeyedId is null):
                return LocalExecutionReleaseInspectionReading.NotRecorded;
            default:
                return LocalExecutionReleaseInspectionReading.Unavailable;
        }
    }
}
