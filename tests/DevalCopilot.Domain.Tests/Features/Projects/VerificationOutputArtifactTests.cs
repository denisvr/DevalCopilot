using DevalCopilot.Domain.Features.Projects;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Projects;

public sealed class VerificationOutputArtifactTests
{
    [Fact]
    public void Recovered_output_has_unknown_truncation_and_an_explicit_interruption_outcome()
    {
        var artifact = VerificationOutputArtifact.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            VerificationOutputPurpose.StandardOutput,
            "verifications/execution/stdout.sealed",
            "sha256:abc",
            3,
            truncated: null,
            captureOutcome: VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption,
            DateTimeOffset.UtcNow);

        Assert.Null(artifact.Truncated);
        Assert.Equal(VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption, artifact.CaptureOutcome);
    }

    [Fact]
    public void Known_capture_requires_and_preserves_the_truncation_value()
    {
        var artifact = VerificationOutputArtifact.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            VerificationOutputPurpose.StandardError,
            "verifications/execution/stderr.sealed",
            "sha256:abc",
            3,
            truncated: false,
            captureOutcome: VerificationOutputCaptureOutcome.CapturedWithKnownTruncation,
            DateTimeOffset.UtcNow);

        Assert.False(artifact.Truncated);
        Assert.Equal(VerificationOutputCaptureOutcome.CapturedWithKnownTruncation, artifact.CaptureOutcome);
    }

    [Fact]
    public void Recovered_output_rejects_an_invented_truncation_value()
    {
        Assert.Throws<ArgumentException>(() => VerificationOutputArtifact.Record(
            Guid.NewGuid(),
            Guid.NewGuid(),
            VerificationOutputPurpose.StandardOutput,
            "verifications/execution/stdout.sealed",
            "sha256:abc",
            3,
            truncated: false,
            captureOutcome: VerificationOutputCaptureOutcome.RecoveredAfterHostInterruption,
            DateTimeOffset.UtcNow));
    }
}
