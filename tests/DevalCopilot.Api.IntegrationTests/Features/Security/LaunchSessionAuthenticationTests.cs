using System.Net;
using System.Net.Http.Headers;
using DevalCopilot.Api.IntegrationTests.Fixtures;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.Security;

public sealed class LaunchSessionAuthenticationTests(ApiWebApplicationFactory factory)
    : IClassFixture<ApiWebApplicationFactory>
{
    private const string ProtectedRoute = "/api/projects/run-summaries";

    [Fact]
    public async Task An_absent_credential_returns_401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ProtectedRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_incorrect_credential_returns_401()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-right-secret");

        var response = await client.GetAsync(ProtectedRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_correct_credential_succeeds()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiWebApplicationFactory.ValidSecret);

        var response = await client.GetAsync(ProtectedRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_correct_secret_supplied_only_as_an_access_token_query_parameter_still_returns_401()
    {
        using var client = factory.CreateClient();
        // Deliberately no Authorization header: the credential is offered only the way
        // some client libraries default to for streaming transports. The handler must
        // never read the query string.
        var response = await client.GetAsync($"{ProtectedRoute}?access_token={ApiWebApplicationFactory.ValidSecret}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
