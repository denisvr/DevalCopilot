namespace DevalCopilot.Api.RealTime;

/// <summary>
/// The client-facing method name and payload shape pushed to <see cref="RunNotificationHub"/>
/// subscribers after a run's durable state advances.
/// </summary>
public sealed record RunAdvancedNotification(Guid RunId, long LatestSequence)
{
    public const string ClientMethodName = "runAdvanced";
}
