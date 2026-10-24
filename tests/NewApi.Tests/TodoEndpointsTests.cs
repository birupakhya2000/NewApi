using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace NewApi.Tests;

public class TodoEndpointsTests : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client;

    public TodoEndpointsTests(TestApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateTodo_ReturnsCreated_AndAppearsInTheList()
    {
        var title = $"Buy milk {Guid.NewGuid():N}";

        var create = await _client.PostAsJsonAsync("/api/todos", new { title });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<TodoResponse>();
        Assert.NotNull(created);
        Assert.Equal(title, created.Title);
        Assert.False(created.IsDone);
        Assert.Equal(TestApiFactory.FixedUtcNow, created.CreatedAtUtc);

        var list = await _client.GetFromJsonAsync<List<TodoResponse>>("/api/todos");
        Assert.Contains(list!, t => t.Id == created.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateTodo_BlankTitle_ReturnsValidationProblem(string title)
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { title });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"errors\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateTodo_TitleTooLong_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/todos", new { title = new string('a', 201) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTodo_RemovesIt_AndSecondDeleteIsNotFound()
    {
        var create = await _client.PostAsJsonAsync("/api/todos", new { title = "Temporary" });
        var created = await create.Content.ReadFromJsonAsync<TodoResponse>();

        var first = await _client.DeleteAsync($"/api/todos/{created!.Id}");
        var second = await _client.DeleteAsync($"/api/todos/{created.Id}");
        var get = await _client.GetAsync($"/api/todos/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task GetTodo_Unknown_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/todos/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_IsServed_InDevelopment()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/todos", await response.Content.ReadAsStringAsync());
    }
}