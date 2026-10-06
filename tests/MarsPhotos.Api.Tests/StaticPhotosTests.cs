using System.Net;
using System.Text.Json;

namespace MarsPhotos.Api.Tests;

public class StaticPhotosTests
{
    private static async Task<string> FirstPhotoUrl(HttpClient client)
    {
        var run = await client.PostAsync("/api/runs", null);
        var root = JsonDocument.Parse(await run.Content.ReadAsStringAsync()).RootElement;
        return root.GetProperty("results")[0].GetProperty("photos")[0].GetProperty("url").GetString()!;
    }

    [Fact]
    public async Task A_stored_photo_is_served_as_an_image()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var url = await FirstPhotoUrl(client);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(12, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Theory]
    [InlineData("/photos/%2e%2e/dates.txt")]
    [InlineData("/photos/2017-02-27/%2e%2e/%2e%2e/dates.txt")]
    [InlineData("/photos/..%2fdates.txt")]
    [InlineData("/photos/2017-02-27/..%5c..%5cdates.txt")]
    public async Task Traversal_attempts_never_reach_files_outside_the_photos_root(string path)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await FirstPhotoUrl(client);

        var response = await client.GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("02/27/17", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Part_files_and_unknown_file_types_are_not_served()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await FirstPhotoUrl(client);
        var folder = Path.Combine(factory.PhotosRoot, "2017-02-27");
        File.WriteAllText(Path.Combine(folder, "half.jpg.part"), "partial");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "secret notes");

        var part = await client.GetAsync("/photos/2017-02-27/half.jpg.part");
        var txt = await client.GetAsync("/photos/2017-02-27/notes.txt");

        Assert.Equal(HttpStatusCode.NotFound, part.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, txt.StatusCode);
    }

    [Fact]
    public async Task Missing_photos_are_404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/photos/2017-02-27/nope.jpg");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_ui_origin_is_allowed_by_cors_and_other_origins_are_not()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var allowed = new HttpRequestMessage(HttpMethod.Get, "/health");
        allowed.Headers.Add("Origin", "http://localhost:4200");
        var other = new HttpRequestMessage(HttpMethod.Get, "/health");
        other.Headers.Add("Origin", "http://evil.example");

        var allowedResponse = await client.SendAsync(allowed);
        var otherResponse = await client.SendAsync(other);

        Assert.True(allowedResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        Assert.Equal("http://localhost:4200", origins!.Single());
        Assert.False(otherResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task A_preflight_for_the_run_endpoint_from_the_ui_origin_succeeds()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/runs");
        preflight.Headers.Add("Origin", "http://localhost:4200");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");

        var response = await client.SendAsync(preflight);

        Assert.True(response.IsSuccessStatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Static_responses_never_contain_the_key()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var url = await FirstPhotoUrl(client);

        var response = await client.GetAsync(url);

        Assert.DoesNotContain(TestKey.Value, response.ToString());
    }
}
