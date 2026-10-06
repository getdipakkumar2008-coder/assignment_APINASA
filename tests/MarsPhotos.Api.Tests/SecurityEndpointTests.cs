using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MarsPhotos.Domain;

namespace MarsPhotos.Api.Tests;

/// <summary>Regression tests for code-review findings MEDIUM-1 (cross-site POST) and MEDIUM-2 (non-JSON body).</summary>
public class SecurityEndpointTests
{
    private static HttpRequestMessage Post(string path, string? origin, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("https://localhost:4200")]
    [InlineData("http://localhost:4201")]
    [InlineData("null")]
    public async Task A_foreign_origin_cannot_start_a_run_and_nothing_is_looked_up(string origin)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/api/runs", origin));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ErrorCodes.ForbiddenOrigin, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        Assert.Empty(factory.Provider.Dates);
        Assert.False(Directory.Exists(Path.Combine(factory.PhotosRoot, "2017-02-27")));
    }

    [Fact]
    public async Task A_foreign_origin_cannot_cancel_a_run()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/api/runs/cancel", "http://evil.example"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("http://localhost")] // the API's own origin: Swagger UI "Try it out" posts from here
    [InlineData(null)]
    public async Task The_ui_origin_the_same_origin_and_non_browser_clients_can_start_a_run(string? origin)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Post("/api/runs", origin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reads_are_not_blocked_by_the_origin_guard()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // CORS keeps the response unreadable for that origin
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("application/xml")]
    public async Task A_body_that_is_not_json_is_a_415_problem_not_a_500(string contentType)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/runs", new StringContent("x=1", Encoding.UTF8, contentType));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(factory.Provider.Dates);
    }

    [Fact]
    public async Task A_json_body_with_a_charset_is_still_accepted()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var content = new StringContent("{\"dates\":[\"02/27/17\"]}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/runs", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_body_less_post_is_not_affected_by_the_content_type_check()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/runs", new { dates = new[] { "02/27/17" } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
