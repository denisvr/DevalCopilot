using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DevalCopilot.Domain.Features.Runs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DevalCopilot.Api.IntegrationTests.Features.ExecutionCapacity;

/// <summary>
/// ADR-0059's explicit limit change through the real host: authentication and the route inventory, the full request matrix (exact body
/// shape, plain integer one or two, no coercion, duplicate, case, escape or extra member, query, content type and the 512-byte bound with
/// and without a declared length), the 409 refusals with their fixed codes and no written revision, the idempotent no-op, the four-member
/// answers under the new limit and the wire tuples the read now states. Requests that cannot dispatch start no delivery setup.
/// </summary>
public sealed partial class ExecutionCapacityEndpointTests
{
    private const string LimitRoute = "/api/execution-capacity/limit";

    private async Task<(HttpResponseMessage Response, string Text)> PutAsync(HttpContent content, string suffix = "")
    {
        using var client = Client();
        var response = await client.PutAsync(LimitRoute + suffix, content);
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private Task<string> HistoryAsync() => WithDbAsync(async db => string.Join(
        ',',
        await db.ExecutionCapacityConfigurations.AsNoTracking().OrderBy(row => row.Revision)
            .Select(row => row.Revision + ":" + row.Limit).ToListAsync()));

    private async Task<JsonElement> ReadAsync()
    {
        using var client = Client();
        var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(Members.Order(StringComparer.Ordinal), body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        return body;
    }

    private static void AssertLimit(JsonElement body, int? limit)
    {
        var member = body.GetProperty("limit");
        if (limit is { } expected)
        {
            Assert.Equal(JsonValueKind.Number, member.ValueKind);
            Assert.Equal(expected, member.GetInt32());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, member.ValueKind);
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    // ---- authentication and routes ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_change_requires_the_launch_session_secret_and_changes_nothing_without_it()
    {
        using var anonymous = factory.CreateClient();
        using var wrong = factory.CreateClient();
        wrong.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync(LimitRoute, Json("{\"limit\":1}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.PutAsync(LimitRoute, Json("{\"limit\":1}"))).StatusCode);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task No_other_method_exists_on_the_limit_route(string method)
    {
        using var client = Client();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), LimitRoute));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    // ---- accepted requests ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lowering_to_one_with_no_owner_returns_the_four_member_reading_and_the_read_states_limit_one()
    {
        var (response, text) = await PutAsync(Json("{\"limit\":1}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(text).RootElement;
        Assert.Equal(Members.Order(StringComparer.Ordinal), body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        AssertLimit(body, 1);
        AssertTuple(body, "Available", 0, null);
        Assert.Equal("1:2,2:1", await HistoryAsync());

        var read = await ReadAsync();
        AssertLimit(read, 1);
        AssertTuple(read, "Available", 0, null);
    }

    [Fact]
    public async Task Lowering_to_one_beside_one_owner_reports_capacity_reached_with_the_fixed_reason()
    {
        await AddAsync(await SeedSceneAsync(), "verification");

        var (response, text) = await PutAsync(Json("{\"limit\":1}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(text).RootElement;
        AssertLimit(body, 1);
        AssertTuple(body, "AtCapacity", 1, "execution_capacity_limit_reached");
        AssertTuple(await ReadAsync(), "AtCapacity", 1, "execution_capacity_limit_reached");
    }

    [Fact]
    public async Task The_same_value_is_a_200_no_op_that_writes_no_revision()
    {
        await AddAsync(await SeedSceneAsync(), "agent");
        await AddAsync(await SeedSceneAsync(), "commit");
        var before = await CountsAsync();

        var (response, text) = await PutAsync(Json("{\"limit\":2}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(text).RootElement;
        AssertLimit(body, 2);
        AssertTuple(body, "AtCapacity", 2, "execution_capacity_limit_reached");
        Assert.Equal("1:2", await HistoryAsync());
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task Insignificant_whitespace_and_a_body_of_exactly_the_bound_are_accepted()
    {
        var padded = "{\"limit\":1}" + new string(' ', 512 - "{\"limit\":1}".Length);
        Assert.Equal(512, Encoding.UTF8.GetByteCount(padded));

        var (response, _) = await PutAsync(Json(padded));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1:2,2:1", await HistoryAsync());
        var (spaced, _) = await PutAsync(Json(" \n{ \"limit\" : 2 }\r\n"));
        Assert.Equal(HttpStatusCode.OK, spaced.StatusCode);
        Assert.Equal("1:2,2:1,3:2", await HistoryAsync());
    }

    // ---- refusals of the request ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("\"limit\"")]
    [InlineData("{}")]
    [InlineData("{\"limit\":null}")]
    [InlineData("{\"limit\":\"1\"}")]
    [InlineData("{\"limit\":1.0}")]
    [InlineData("{\"limit\":1.5}")]
    [InlineData("{\"limit\":1e0}")]
    [InlineData("{\"limit\":2E0}")]
    [InlineData("{\"limit\":true}")]
    [InlineData("{\"limit\":false}")]
    [InlineData("{\"limit\":[1]}")]
    [InlineData("{\"limit\":{\"a\":1}}")]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":3}")]
    [InlineData("{\"limit\":-1}")]
    [InlineData("{\"limit\":-0}")]
    [InlineData("{\"limit\":10}")]
    [InlineData("{\"limit\":01}")]
    [InlineData("{\"limit\":+1}")]
    [InlineData("{\"limit\":2147483648}")]
    [InlineData("{\"limit\":99999999999999999999}")]
    [InlineData("{\"limit\":1,\"limit\":2}")]
    [InlineData("{\"limit\":1,\"extra\":1}")]
    [InlineData("{\"extra\":1,\"limit\":1}")]
    [InlineData("{\"Limit\":1}")]
    [InlineData("{\"LIMIT\":1}")]
    [InlineData("{\"lim\\u0069t\":1}")]
    [InlineData("{\"limit\":1}garbage")]
    [InlineData("{\"limit\":1}{}")]
    [InlineData("{\"limit\":1")]
    [InlineData("{\"limit\":}")]
    [InlineData("{limit:1}")]
    [InlineData("{'limit':1}")]
    [InlineData("{\"limit\":1,}")]
    [InlineData("{\"limit\":1}//c")]
    [InlineData("[{\"limit\":1}]")]
    [InlineData("{\"nested\":{\"limit\":1}}")]
    public async Task A_body_that_is_not_exactly_one_plain_limit_of_one_or_two_is_refused_before_dispatch(string body)
    {
        var (response, text) = await PutAsync(Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("execution_capacity.invalid_request", text, StringComparison.Ordinal);
        Assert.Equal("1:2", await HistoryAsync());
        foreach (var fragment in new[] { "garbage", "extra", "nested" }.Where(body.Contains))
        {
            Assert.DoesNotContain(fragment, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_byte_order_mark_or_a_non_utf8_body_is_refused()
    {
        using var bom = new ByteArrayContent([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("{\"limit\":1}")]);
        bom.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var invalid = new ByteArrayContent([0xFF, 0xFE, 0x7B]);
        invalid.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(bom)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(invalid)).Response.StatusCode);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Theory]
    [InlineData("?limit=1")]
    [InlineData("?a=1")]
    [InlineData("?x")]
    public async Task A_query_string_is_refused_and_nothing_is_changed(string query)
    {
        var (response, text) = await PutAsync(Json("{\"limit\":1}"), query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("execution_capacity.invalid_request", text, StringComparison.Ordinal);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    [InlineData("application/x-www-form-urlencoded")]
    public async Task An_unsupported_media_type_is_a_415_and_nothing_is_changed(string mediaType)
    {
        using var content = new StringContent("{\"limit\":1}", Encoding.UTF8, mediaType);

        var (response, _) = await PutAsync(content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Fact]
    public async Task A_body_without_a_content_type_is_refused_and_nothing_is_changed()
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"limit\":1}"));

        var (response, _) = await PutAsync(content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Fact]
    public async Task A_declared_body_over_512_bytes_is_a_413_with_the_fixed_code_and_is_not_echoed()
    {
        var oversized = "{\"limit\":1}" + new string(' ', 513 - "{\"limit\":1}".Length);

        var (response, text) = await PutAsync(Json(oversized));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("execution_capacity.body_too_large", text, StringComparison.Ordinal);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Theory]
    [InlineData(513)]
    [InlineData(100_000)]
    public async Task A_body_without_a_declared_length_over_512_bytes_is_a_413_and_reads_no_more_than_the_bound(int size)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"limit\":1}" + new string(' ', size - "{\"limit\":1}".Length));
        using var content = new UnknownLengthContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var (response, text) = await PutAsync(content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("execution_capacity.body_too_large", text, StringComparison.Ordinal);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Fact]
    public async Task A_body_without_a_declared_length_within_the_bound_is_accepted()
    {
        using var content = new UnknownLengthContent(Encoding.UTF8.GetBytes("{\"limit\":1}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var (response, _) = await PutAsync(content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1:2,2:1", await HistoryAsync());
    }

    // ---- refusals of the command ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Lowering_two_to_one_while_two_owners_exist_is_a_409_with_the_fixed_code_and_writes_nothing()
    {
        var first = await SeedSceneAsync();
        var second = await SeedSceneAsync();
        await AddAsync(first, "agent");
        await AddAsync(second, "verification");
        var before = await CountsAsync();

        var (response, text) = await PutAsync(Json("{\"limit\":1}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("execution_capacity.limit_below_recorded_owners", text, StringComparison.Ordinal);
        Assert.DoesNotContain(first.ProjectId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("1:2", await HistoryAsync());
        Assert.Equal(before, await CountsAsync());
        AssertTuple(await ReadAsync(), "AtCapacity", 2, "execution_capacity_limit_reached");
    }

    [Fact]
    public async Task Untrustworthy_ownership_makes_the_change_a_409_unavailable_and_writes_nothing()
    {
        var scene = await SeedSceneAsync();
        var attemptId = await AddAgentAsync(scene);
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"ZzUnrecognized"} WHERE Id = {attemptId}");
            return 0;
        });

        var (response, text) = await PutAsync(Json("{\"limit\":1}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("execution_capacity.unavailable", text, StringComparison.Ordinal);
        Assert.Equal("1:2", await HistoryAsync());
    }

    [Fact]
    public async Task A_missing_configuration_is_a_409_unavailable_and_the_read_states_a_null_limit_and_null_count()
    {
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM execution_capacity_configurations");
            return 0;
        });

        var (response, text) = await PutAsync(Json("{\"limit\":1}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("execution_capacity.unavailable", text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, await HistoryAsync());
        var read = await ReadAsync();
        AssertLimit(read, null);
        AssertTuple(read, "Unavailable", null, "execution_capacity_unavailable");
    }

    [Fact]
    public async Task Unsafe_ownership_keeps_the_validated_limit_in_the_read_while_a_missing_configuration_does_not()
    {
        var scene = await SeedSceneAsync();
        var attemptId = await AddAgentAsync(scene);
        await PutAsync(Json("{\"limit\":1}"));
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attempts SET AgentRole = {"ZzUnrecognized"} WHERE Id = {attemptId}");
            return 0;
        });

        var read = await ReadAsync();

        AssertLimit(read, 1);
        AssertTuple(read, "Unavailable", null, "execution_capacity_unavailable");
    }

    [Fact]
    public async Task Concurrent_changes_are_serialized_and_each_answers_a_coherent_reading()
    {
        using var first = Client();
        using var second = Client();

        var answers = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second)
                .PutAsync(LimitRoute, Json(index % 2 == 0 ? "{\"limit\":1}" : "{\"limit\":2}"))));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.OK, answer.StatusCode));
        var history = (await HistoryAsync()).Split(',');
        Assert.Equal(Enumerable.Range(1, history.Length).Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            history.Select(row => row.Split(':')[0]));
        var read = await ReadAsync();
        AssertLimit(read, int.Parse(history[^1].Split(':')[1], System.Globalization.CultureInfo.InvariantCulture));
    }
}
