using DevalCopilot.Application.Features.Runs.Commands.RecordChallengeResolutionResult;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>
/// Proves <see cref="ValidatedChallengeResolution.Decisions"/> is genuinely immutable once
/// constructed — a caller that mutates the list it passed to <c>Create</c> after the call must
/// never be able to change what this "already validated" value reports, since
/// <c>RecordChallengeResolutionResultCommandHandler</c> trusts its count and contents without
/// re-reading the caller's own collection.
/// </summary>
public sealed class ValidatedChallengeResolutionTests
{
    private static ValidatedRevisedProposal RevisedProposal() => new("Revised summary", """{"scope":"s"}""");

    [Fact]
    public void Create_defensively_copies_the_supplied_decisions_so_a_later_mutation_of_the_callers_list_is_invisible()
    {
        var challengeId = Guid.NewGuid();
        var callerOwnedList = new List<ValidatedDecision>
        {
            new(challengeId, "Decision summary", """{"resolution":"accepted"}"""),
        };

        var resolution = ValidatedChallengeResolution.Create("Overall summary", callerOwnedList, RevisedProposal());

        // Mutating the caller's own list after Create must never be visible through the
        // already-constructed value.
        callerOwnedList.Add(new ValidatedDecision(Guid.NewGuid(), "Injected after Create", """{"resolution":"accepted"}"""));
        callerOwnedList.Clear();

        Assert.Single(resolution.Decisions);
        Assert.Equal(challengeId, resolution.Decisions[0].ChallengeMessageId);
    }
}
