namespace DevalCopilot.Application.Tests.Features.Projects.ProjectVerificationInspection;

internal sealed record StoreCall(string Path, long Length, string Hash, long Offset, int MaxBytes);
