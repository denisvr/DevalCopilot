namespace DevalCopilot.Domain.Features.Projects;

/// <summary>The kind of trusted reviewer that recorded a review fact.</summary>
public enum ReviewActorKind
{
    Human = 0,
    FutureAgent = 1,
}
