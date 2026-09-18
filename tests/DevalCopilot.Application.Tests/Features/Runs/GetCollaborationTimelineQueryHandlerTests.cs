using DevalCopilot.Application.Features.Runs.Queries.GetCollaborationTimeline;
using DevalCopilot.Domain.Features.Projects;
using DevalCopilot.Domain.Features.Runs;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

public sealed class GetCollaborationTimelineQueryHandlerTests(SqliteDatabaseFixture fixture)
    : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_reads_a_deterministic_bounded_timeline_from_the_database()
    {
        await using var dbContext = fixture.CreateContext();
        var project = Project.Register(Guid.NewGuid(), "DevalCopilot", $@"C:\repos\{Guid.NewGuid():N}", Now);
        var run = Run.RecordIntent(Guid.NewGuid(), project.Id, 1, "Read ledger", Now);
        dbContext.AddRange(project, run);
        for (var index = 0; index < GetCollaborationTimelineQueryHandler.MaximumResults + 2; index++)
        {
            dbContext.CollaborationMessages.Add(CollaborationMessage.Record(
                Guid.NewGuid(), run.Id, null, CollaborationMessage.ProtocolVersionOne,
                ParticipantKind.Codex, ParticipantKind.Claude, CollaborationMessageType.Proposal, null,
                $"Proposal {index}", ProposalContent, CollaborationMessageProvenance.Simulated, Now.AddSeconds(index)));
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        var handler = new GetCollaborationTimelineQueryHandler(dbContext);
        var result = await handler.HandleAsync(new GetCollaborationTimelineQuery(run.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(GetCollaborationTimelineQueryHandler.MaximumResults, result.Value.Count);
        Assert.Equal("Proposal 2", result.Value[0].Summary);
        Assert.True(result.Value.Zip(result.Value.Skip(1), (left, right) => left.Sequence < right.Sequence).All(value => value));
    }

    private const string ProposalContent =
        "{\"scope\":\"Ledger\",\"implementationSteps\":\"Add the table then the query\",\"risks\":\"Unbounded content\",\"verificationPlan\":\"Tests\",\"escalationPoints\":\"None expected\"}";
}
