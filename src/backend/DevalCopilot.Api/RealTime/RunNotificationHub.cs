using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DevalCopilot.Api.RealTime;

/// <summary>
/// Broadcast-only notification channel. It carries no business commands and is not
/// durable transport; clients always catch up through the authoritative query API.
/// </summary>
[Authorize]
public sealed class RunNotificationHub : Hub;
