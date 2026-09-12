using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using DevalCopilot.Application.Security.Ports;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DevalCopilot.Api.Security;

/// <summary>
/// Validates <c>Authorization: Bearer &lt;session-secret&gt;</c> against the current
/// launch's secret. Deliberately reads only the Authorization header: the query string
/// is never inspected, so an <c>access_token</c> parameter is never accepted.
/// </summary>
public sealed class LaunchSessionAuthenticationHandler(
    IOptionsMonitor<LaunchSessionAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ILaunchSessionAccessor launchSession)
    : AuthenticationHandler<LaunchSessionAuthenticationOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization header."));
        }

        var headerValue = headerValues.ToString();
        if (!headerValue.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.Fail("Authorization header is not a Bearer credential."));
        }

        var candidate = headerValue[BearerPrefix.Length..];
        if (!FixedTimeEquals(candidate, launchSession.Secret))
        {
            return Task.FromResult(AuthenticateResult.Fail("The bearer credential is not valid."));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "launch-session")], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    private static bool FixedTimeEquals(string candidate, string expected)
    {
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return candidateBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }
}
