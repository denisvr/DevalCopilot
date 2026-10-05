using DevalCopilot.Application.Features.Projects.Ports;
using DevalCopilot.Application.Features.Runs.Policies;
using DevalCopilot.Application.Features.Runs.Ports;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>The observable result of one claim attempt against a Codex claim handler: whether it succeeded, the stable error code of a
/// refusal, how many Git captures it started and how many sealed manifests it had to remove again.</summary>
public sealed record AccountUsageClaimOutcome(bool Success, string? ErrorCode, int GitCaptures, int OrphanedManifests);
