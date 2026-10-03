namespace DevalCopilot.Application.Tests.Features.Runs;

/// <summary>Which complete canonical serialization a seeded escalation carries.</summary>
public enum EscalationForm
{
    /// <summary>Whatever the production writer records now.</summary>
    Writer,

    /// <summary>The historical serialization written before ADR-0020, reproduced here independently of production.</summary>
    Legacy,

    /// <summary>The ADR-0020 serialization, reproduced here independently of the production writer.</summary>
    Current,
}
