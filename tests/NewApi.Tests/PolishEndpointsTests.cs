using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace NewApi.Tests;

public class PolishEndpointsTests
{
    [Fact]
    public async Task Ready_returns_200_when_database_is_reachable()
    {
        using var factory = new TestApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Version_returns_a_version_string()
    {
        using var factory = new TestApiFactory();
        var client = factory.CreateClient();

        var json = await client.GetFromJsonAsync<JsonElement>("/version");

        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task Unknown_route_returns_problem_json()
    {
        using var factory = new TestApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}