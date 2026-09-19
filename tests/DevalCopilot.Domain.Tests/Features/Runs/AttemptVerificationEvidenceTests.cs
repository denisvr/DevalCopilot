using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Domain.Tests.Features.Runs;

public sealed class AttemptVerificationEvidenceTests
{
    [Fact]
    public void Record_creates_a_valid_membership_row()
    {
        var attemptId = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var evidence = AttemptVerificationEvidence.Record(Guid.NewGuid(), attemptId, commandId, executionId, sequence: 2);

        Assert.Equal(attemptId, evidence.AttemptId);
        Assert.Equal(commandId, evidence.VerificationCommandId);
        Assert.Equal(executionId, evidence.VerificationExecutionId);
        Assert.Equal(2, evidence.Sequence);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Record_rejects_an_empty_identifier(bool emptyId, bool emptyAttemptId, bool emptyCommandId, bool emptyExecutionId)
    {
        Assert.Throws<ArgumentException>(() => AttemptVerificationEvidence.Record(
            emptyId ? Guid.Empty : Guid.NewGuid(),
            emptyAttemptId ? Guid.Empty : Guid.NewGuid(),
            emptyCommandId ? Guid.Empty : Guid.NewGuid(),
            emptyExecutionId ? Guid.Empty : Guid.NewGuid(),
            sequence: 0));
    }

    [Fact]
    public void Record_rejects_a_negative_sequence()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AttemptVerificationEvidence.Record(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), sequence: -1));
    }
}
