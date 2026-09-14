using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class ArtifactTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Record_creates_an_artifact_with_the_given_fields()
    {
        var artifact = Artifact.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ArtifactPurpose.ProcessStandardOutput,
            "text/plain; charset=utf-8",
            @"runs\r\attempts\a\stdout.sealed",
            "sha256:abc123",
            42,
            truncated: true,
            ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.RedactedBestEffort,
            ArtifactRetentionPolicy.RetainUntilRunDeleted,
            Now);

        Assert.Equal(ArtifactPurpose.ProcessStandardOutput, artifact.Purpose);
        Assert.Equal(42, artifact.ByteLength);
        Assert.True(artifact.Truncated);
        Assert.Equal(ArtifactCaptureOutcome.Captured, artifact.CaptureOutcome);
        Assert.Equal(ArtifactSensitivity.RedactedBestEffort, artifact.Sensitivity);
        Assert.Equal(ArtifactRetentionPolicy.RetainUntilRunDeleted, artifact.RetentionPolicy);
        Assert.Equal(Now, artifact.CreatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Record_rejects_a_missing_media_type(string? mediaType)
    {
        Assert.ThrowsAny<ArgumentException>(() => Artifact.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ArtifactPurpose.ProcessStandardOutput,
            mediaType!, "path", "sha256:abc", 1, false, ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
    }

    [Fact]
    public void Record_rejects_a_negative_byte_length()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Artifact.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ArtifactPurpose.ProcessStandardOutput,
            "text/plain", "path", "sha256:abc", -1, false, ArtifactCaptureOutcome.Captured,
            ArtifactSensitivity.RedactedBestEffort, ArtifactRetentionPolicy.RetainUntilRunDeleted, Now));
    }
}
