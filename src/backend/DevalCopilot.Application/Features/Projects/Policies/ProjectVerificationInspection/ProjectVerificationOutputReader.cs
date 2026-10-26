using DevalCopilot.Application.Data;
using DevalCopilot.Application.Features.Runs.Policies.AgentAttemptVerification;
using DevalCopilot.Domain.Features.Projects;
using Microsoft.EntityFrameworkCore;

namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>
/// Resolves the metadata and the storage of exactly one allowlisted output stream of one proved execution (ADR-0069), and admits it with
/// the existing <see cref="RecordedVerificationOutputAdmission"/> unchanged. It reads at most two rows of the exact execution and purpose,
/// so a second row proves damaged storage and neither stream hides its sibling, and it reads identities as raw bytes so a damaged
/// artifact identity is Unavailable rather than a conversion fault. The descriptor read selects no path, hash or byte; only a window's
/// storage read selects the stored path and hash, which it passes, unchanged, to the artifact store's own proof.
/// </summary>
internal static class ProjectVerificationOutputReader
{
    public static async Task<RecordedVerificationOutputDescriptor> DescribeAsync(
        IDevalCopilotDbContext dbContext, Guid executionId, VerificationOutputPurpose purpose, CancellationToken cancellationToken)
    {
        var rows = await dbContext.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == executionId && output.Purpose == purpose)
            .WhereAllowlisted()
            .Take(2)
            .ProjectDescriptor()
            .ToArrayAsync(cancellationToken);
        var code = RecordedVerificationOutputAdmission.PurposeCode(purpose);
        return rows.Any(row => row.PurposeCode != code || ProjectVerificationInspectionStorage.ParseIdentifier(row.ExecutionId) != executionId)
            ? RecordedVerificationOutputDescriptor.Unavailable
            : RecordedVerificationOutputAdmission.Admit(rows.Select(row => Adapt(row)).ToArray());
    }

    public static async Task<ProjectVerificationOutputStorageReading> ReadStorageAsync(
        IDevalCopilotDbContext dbContext, Guid executionId, VerificationOutputPurpose purpose, CancellationToken cancellationToken)
    {
        var rows = await dbContext.VerificationOutputArtifacts.AsNoTracking()
            .Where(output => output.VerificationExecutionId == executionId && output.Purpose == purpose)
            .WhereAllowlisted()
            .Take(2)
            .ProjectStorage()
            .ToArrayAsync(cancellationToken);
        var code = RecordedVerificationOutputAdmission.PurposeCode(purpose);
        if (rows.Any(row => row.PurposeCode != code || ProjectVerificationInspectionStorage.ParseIdentifier(row.ExecutionId) != executionId))
        {
            return new ProjectVerificationOutputStorageReading(RecordedVerificationOutputDescriptor.Unavailable, null);
        }

        var shared = rows.Select(Adapt).ToArray();
        var descriptor = RecordedVerificationOutputAdmission.Admit(shared.Select(row => row.Descriptor).ToArray());
        return new ProjectVerificationOutputStorageReading(
            descriptor, descriptor.Status == RecordedVerificationOutputDescriptorStatus.Recorded ? shared[0] : null);
    }

    private static VerificationOutputDescriptorRow Adapt(ProjectVerificationOutputDescriptorRow row) => new(
        ProjectVerificationInspectionStorage.ParseIdentifier(row.Id) ?? Guid.Empty,
        ProjectVerificationInspectionStorage.ParseIdentifier(row.ExecutionId) ?? Guid.Empty,
        row.PurposeCode,
        row.ByteLength,
        row.CaptureCode,
        row.TruncatedStored,
        row.TruncatedPresent);

    private static VerificationOutputStorageRow Adapt(ProjectVerificationOutputStorageRow row) => new(
        ProjectVerificationInspectionStorage.ParseIdentifier(row.Id) ?? Guid.Empty,
        ProjectVerificationInspectionStorage.ParseIdentifier(row.ExecutionId) ?? Guid.Empty,
        row.PurposeCode,
        row.ByteLength,
        row.CaptureCode,
        row.TruncatedStored,
        row.TruncatedPresent,
        row.RelativeStoragePath,
        row.RelativeStoragePathOversized,
        row.ContentHash,
        row.ContentHashOversized);
}
