namespace DevalCopilot.Application.Features.Projects.Policies.ProjectVerificationInspection;

/// <summary>The scalar facts of one history page row (ADR-0069): the raw stored identity and claim time within their bounds, the number
/// only when the database proved it a positive 32-bit integer and the snapshot command name within its byte guard. No path, argument,
/// executable, status or artifact fact is part of it.</summary>
internal sealed record ProjectVerificationHistoryRow(
    byte[]? Id, int? Number, string? CommandName, bool CommandNameOversized, byte[]? ClaimedAtUtc);
