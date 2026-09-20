using DevalCopilot.Domain.Features.Runs;

namespace DevalCopilot.Api.Features.Runs;

public sealed record ParticipantIdentityResponse(string Kind, string? Role, string? Provider)
{
    public static ParticipantIdentityResponse FromDomain(ParticipantIdentity participant) =>
        new(participant.Kind.ToString(), participant.Role?.ToString(), participant.Provider?.ToString());
}
