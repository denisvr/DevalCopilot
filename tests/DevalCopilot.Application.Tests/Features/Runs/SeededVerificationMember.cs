using DevalCopilot.Domain.Features.Projects;

namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>One seeded member: the recipe that was configured and the execution that snapshotted it.</summary>
internal sealed record SeededVerificationMember(VerificationCommand Command, VerificationExecution Execution);
