using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Application.Features.Projects.Queries.GetProjectRunHistory;

/// <summary>One recorded run of the project. <see cref="Lifecycle"/> and <see cref="Stage"/> are null when the stored value is not one
/// this version recognizes, and <see cref="ExecutionMode"/> is then <see cref="RunExecutionModeStorage.Unrecognized"/>; the row is kept
/// and nothing is coerced into a known state.</summary>
public sealed record ProjectRunHistoryEntry(
    Guid RunId,
    int ExecutionNumber,
    string Objective,
    RunLifecycle? Lifecycle,
    RunStage? Stage,
    RunExecutionMode ExecutionMode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastAdvancedAtUtc,
    ProjectRunHistoryReceiptSource? ReceiptSource);
