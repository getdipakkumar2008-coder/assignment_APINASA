using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Domain;

namespace MarsPhotos.Api.Tests;

public class RunsEndpointTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    [Trait("AC", "AC-7")]
    public async Task Post_without_a_body_runs_the_configured_file_and_returns_the_summary()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/runs", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Json(response);
        Assert.Equal("curiosity", root.GetProperty("rover").GetString());
        var results = root.GetProperty("results");
        Assert.Equal(4, results.GetArrayLength());
        Assert.Equal(["Success", "Success", "Success", "InvalidDate"], results.EnumerateArray().Select(r => r.GetProperty("status").GetString()));
        Assert.Equal("2017-02-27", results[0].GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Null, results[3].GetProperty("date").ValueKind);
        Assert.Equal(15, root.GetProperty("totals").GetProperty("downloaded").GetInt32());
        Assert.Equal(5, results[0].GetProperty("downloaded").GetInt32());
        Assert.StartsWith("/photos/2017-02-27/", results[0].GetProperty("photos")[0].GetProperty("url").GetString());
        Assert.Equal("StubProvider", results[0].GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Post_with_dates_in_the_body_uses_them_instead_of_the_file()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/runs", new { dates = new[] { "02/27/17", "abc" }, photosPerDate = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = (await Json(response)).GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Equal("UnrecognizedDate", results[1].GetProperty("status").GetString());
        Assert.Single(factory.Provider.Dates);
    }

    [Fact]
    public async Task A_second_run_reports_existing_photos_as_skipped()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await client.PostAsync("/api/runs", null);

        var second = await Json(await client.PostAsync("/api/runs", null));

        Assert.Equal(0, second.GetProperty("totals").GetProperty("downloaded").GetInt32());
        Assert.Equal(15, second.GetProperty("totals").GetProperty("skipped").GetInt32());
    }

    [Fact]
    public async Task More_than_100_dates_is_a_400_problem()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/runs", new { dates = Enumerable.Repeat("02/27/17", 101) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("100", (await Json(response)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_line_longer_than_200_characters_is_a_400_problem()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/runs", new { dates = new[] { new string('9', 201) } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("200", (await Json(response)).GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("{\"rover\":\"sojourner\"}")]
    [InlineData("{\"photosPerDate\":2}")]
    [InlineData("{\"photosPerDate\":6}")]
    [InlineData("{\"dates\":[]}")]
    public async Task Invalid_request_values_are_400_problems(string body)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/runs", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Malformed_json_is_a_400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/runs", new StringContent("{ nope", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task A_missing_input_file_is_a_500_problem_with_the_error_code()
    {
        using var factory = new ApiFactory(datesText: null);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/runs", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ErrorCodes.InputFileError, (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unexpected_errors_are_a_generic_500_that_never_leaks_details_or_the_key()
    {
        var provider = new StubProvider((_, _, _, _) => throw new InvalidOperationException($"boom api_key={TestKey.Value}"));
        using var factory = new ApiFactory(provider);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/runs", null);

        // a provider bug is contained per date, so the run still completes
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestKey.Value, body);
        Assert.Contains("\"Failed\"", body);
    }

    [Fact]
    public async Task Latest_is_404_before_any_run_and_the_last_summary_afterwards()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var before = await client.GetAsync("/api/runs/latest");
        await client.PostAsync("/api/runs", null);
        var after = await client.GetAsync("/api/runs/latest");

        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Equal(4, (await Json(after)).GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Health_is_ok()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("AC", "AC-8")]
    public async Task The_key_never_appears_in_any_response()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var run = await client.PostAsync("/api/runs", null);
        var latest = await client.GetAsync("/api/runs/latest");
        var bad = await client.PostAsJsonAsync("/api/runs", new { rover = "x" });

        foreach (var response in new[] { run, latest, bad })
        {
            var text = response.ToString() + await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(TestKey.Value, text);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Startup_without_a_key_is_refused_with_guidance(string? key)
    {
        using var factory = new ApiFactory(apiKey: key);

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("NASA__ApiKey", ex.ToString());
        Assert.DoesNotContain(TestKey.Value, ex.ToString());
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task A_second_run_while_one_is_active_is_refused_with_409()
    {
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        var provider = new StubProvider(async (rover, date, _, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new ProviderResult.Photos(StubProvider.Photos(rover, date, 3), 3) { Provider = "StubProvider" };
        });
        using var factory = new ApiFactory(provider);
        using var client = factory.CreateClient();

        var first = client.PostAsync("/api/runs", null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = await client.PostAsync("/api/runs", null);
        release.SetResult();
        var firstResponse = await first;

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ErrorCodes.RunInProgress, (await Json(second)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task Cancel_stops_the_active_run_and_marks_unprocessed_dates_cancelled()
    {
        var entered = new TaskCompletionSource();
        var provider = new StubProvider(async (rover, date, _, ct) =>
        {
            if (date.Year == 2018)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }

            return new ProviderResult.Photos(StubProvider.Photos(rover, date, 3), 3) { Provider = "StubProvider" };
        });
        using var factory = new ApiFactory(provider);
        using var client = factory.CreateClient();

        var run = client.PostAsync("/api/runs", null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var cancel = await client.PostAsync("/api/runs/cancel", null);
        var response = await run;

        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var statuses = (await Json(response)).GetProperty("results").EnumerateArray().Select(r => r.GetProperty("status").GetString()).ToList();
        Assert.Equal(["Success", "Cancelled", "Cancelled", "Cancelled"], statuses);
        var latest = await Json(await client.GetAsync("/api/runs/latest"));
        Assert.Equal("Cancelled", latest.GetProperty("results")[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancel_without_an_active_run_is_a_409()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/runs/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ErrorCodes.NoRunInProgress, (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_new_run_is_possible_after_a_previous_one_finished()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var first = await client.PostAsync("/api/runs", null);
        var second = await client.PostAsync("/api/runs", null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }
}
