using System.Net;
using MarsPhotos.Application;
using MarsPhotos.Application.Abstractions;
using MarsPhotos.Application.Providers;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Providers;

public class SolConverterTests
{
    [Theory]
    [InlineData("2017-02-27", 1621)]
    [InlineData("2018-06-02", 2069)]
    [InlineData("2016-07-13", 1398)]
    [InlineData("2012-08-06", 0)]
    [InlineData("2012-08-07", 1)]
    [Trait("Decision", "D-1")]
    public void Maps_earth_dates_to_curiosity_sols(string date, int sol)
    {
        Assert.Equal(sol, SolConverter.ForCuriosity(DateOnly.Parse(date)));
    }

    [Fact]
    public void Dates_before_landing_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SolConverter.ForCuriosity(new DateOnly(2012, 8, 5)));
    }

    [Fact]
    public void Sols_increase_monotonically_and_never_skip_more_than_one_per_day()
    {
        var previous = SolConverter.ForCuriosity(new DateOnly(2012, 8, 6));
        for (var d = new DateOnly(2012, 8, 7); d < new DateOnly(2013, 8, 6); d = d.AddDays(1))
        {
            var sol = SolConverter.ForCuriosity(d);
            Assert.InRange(sol - previous, 0, 1);
            previous = sol;
        }
    }
}

public class PhotoOrderingTests
{
    private static RoverPhoto P(string id) => new(id, null, "C", new Uri("https://mars.nasa.gov/a.jpg"), new DateOnly(2017, 2, 27), "curiosity");

    [Fact]
    public void Numeric_ids_sort_numerically()
    {
        var sorted = PhotoOrdering.Sort([P("102693"), P("99999"), P("100000"), P("7")]);

        Assert.Equal(["7", "99999", "100000", "102693"], sorted.Select(p => p.Id));
    }

    [Fact]
    public void Mixed_or_text_ids_sort_ordinally()
    {
        var sorted = PhotoOrdering.Sort([P("b2"), P("B1"), P("a10"), P("a9")]);

        Assert.Equal(["B1", "a10", "a9", "b2"], sorted.Select(p => p.Id));
    }
}

public class MarsRoverPhotosProviderTests
{
    private static readonly DateOnly Day = new(2017, 2, 27);
    private const string Key = "test-key-0000";

    private static (MarsRoverPhotosProvider Provider, RecordingHandler Handler, CapturingLogger<MarsRoverPhotosProvider> Log) Build(RecordingHandler handler)
    {
        var log = new CapturingLogger<MarsRoverPhotosProvider>();
        var provider = new MarsRoverPhotosProvider(
            new StubHttpClientFactory(handler), Options.Create(new NasaOptions { ApiKey = Key }), log);
        return (provider, handler, log);
    }

    [Fact]
    [Trait("AC", "AC-3")]
    public async Task Returns_photos_sorted_numerically_and_truncated_with_the_available_count()
    {
        var (provider, handler, _) = Build(RecordingHandler.Json(Fixture.Text("primary_photos_ok.json")));

        var result = await provider.GetPhotosAsync("curiosity", Day, 3, CancellationToken.None);

        var photos = Assert.IsType<ProviderResult.Photos>(result);
        Assert.Equal(["7", "55", "99999"], photos.Selected.Select(p => p.Id));
        Assert.Equal(7, photos.Available);
        Assert.Equal("MarsRoverPhotos", photos.Provider);
        Assert.All(photos.Selected, p => Assert.Equal(Day, p.EarthDate));
        Assert.Equal("CHEMCAM", photos.Selected[0].Camera);
        Assert.Equal(1621, photos.Selected[0].Sol);
        Assert.Contains("earth_date=2017-02-27", handler.Requests.Single().Query);
        Assert.Contains("/rovers/curiosity/photos", handler.Requests.Single().AbsolutePath);
    }

    [Fact]
    public async Task Sends_the_key_but_never_logs_it()
    {
        var (provider, handler, log) = Build(RecordingHandler.Json(Fixture.Text("primary_photos_ok.json")));

        await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.Contains($"api_key={Key}", handler.Requests.Single().Query);
        Assert.DoesNotContain(Key, string.Join('\n', log.Messages));
    }

    [Fact]
    public async Task Empty_photo_list_is_no_photos_not_an_error()
    {
        var (provider, _, _) = Build(RecordingHandler.Json(Fixture.Text("primary_empty.json")));

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var none = Assert.IsType<ProviderResult.NoPhotos>(result);
        Assert.Equal("MarsRoverPhotos", none.Provider);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task The_live_no_such_app_page_is_unavailable_after_exactly_one_request()
    {
        var (provider, handler, _) = Build(RecordingHandler.Html(Fixture.Text("primary_no_such_app.html"), HttpStatusCode.NotFound));

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var unavailable = Assert.IsType<ProviderResult.Unavailable>(result);
        Assert.Equal(ErrorCodes.ApiUnavailable, unavailable.ErrorCode);
        Assert.Contains("404", unavailable.Message);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("<!DOCTYPE html><html>not json</html>")]
    [InlineData("{ this is not json")]
    [InlineData("{\"errors\":\"nope\"}")]
    [InlineData("{\"photos\":\"not-an-array\"}")]
    [InlineData("")]
    [InlineData("[]")]
    public async Task Malformed_or_non_data_200_responses_are_bad_responses(string body)
    {
        var (provider, _, _) = Build(RecordingHandler.Json(body));

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var unavailable = Assert.IsType<ProviderResult.Unavailable>(result);
        Assert.Equal(ErrorCodes.ApiBadResponse, unavailable.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ErrorCodes.ApiAuth)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCodes.ApiAuth)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorCodes.ApiRateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorCodes.ApiUnavailable)]
    [InlineData(HttpStatusCode.BadGateway, ErrorCodes.ApiUnavailable)]
    [InlineData(HttpStatusCode.BadRequest, ErrorCodes.ApiBadResponse)]
    [InlineData(HttpStatusCode.MovedPermanently, ErrorCodes.ApiBadResponse)] // redirects are never followed: the key is in the URL
    [InlineData(HttpStatusCode.Found, ErrorCodes.ApiBadResponse)]
    public async Task Http_failures_map_to_stable_error_codes(HttpStatusCode status, string expectedCode)
    {
        var (provider, _, _) = Build(RecordingHandler.Json("{}", status));

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.Equal(expectedCode, Assert.IsType<ProviderResult.Unavailable>(result).ErrorCode);
    }

    [Fact]
    public async Task Network_errors_and_timeouts_are_unavailable_not_exceptions()
    {
        var (provider, _, _) = Build(new RecordingHandler((_, _) => throw new HttpRequestException("no route to host")));
        var (timeoutProvider, _, _) = Build(new RecordingHandler((_, _) => throw new TaskCanceledException("timed out")));

        var network = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);
        var timeout = await timeoutProvider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.Equal(ErrorCodes.ApiUnavailable, Assert.IsType<ProviderResult.Unavailable>(network).ErrorCode);
        Assert.Equal(ErrorCodes.ApiUnavailable, Assert.IsType<ProviderResult.Unavailable>(timeout).ErrorCode);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (provider, _, _) = Build(new RecordingHandler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetPhotosAsync("curiosity", Day, 5, cts.Token));
    }

    [Fact]
    public async Task Tolerates_missing_optional_fields_and_skips_unusable_photos()
    {
        const string body = """
            {"photos":[
              {"id":1,"img_src":"https://mars.nasa.gov/a.jpg"},
              {"id":2},
              {"img_src":"https://mars.nasa.gov/b.jpg"},
              {"id":4,"img_src":"not a url"},
              {"id":5,"sol":9,"camera":{"name":"NAVCAM"},"img_src":"https://mars.nasa.gov/e.jpg","earth_date":"2017-02-27","new_field":[1,2,3]}
            ]}
            """;
        var (provider, _, _) = Build(RecordingHandler.Json(body));

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var photos = Assert.IsType<ProviderResult.Photos>(result);
        Assert.Equal(["1", "5"], photos.Selected.Select(p => p.Id));
        Assert.Equal("UNK", photos.Selected[0].Camera);
        Assert.Equal(2, photos.Available);
    }
}

public class RawImageFeedProviderTests
{
    private static readonly DateOnly Day = new(2017, 2, 27);

    private static (RawImageFeedProvider Provider, RecordingHandler Handler) Build(RecordingHandler handler) =>
        (new RawImageFeedProvider(
            new StubHttpClientFactory(handler),
            Options.Create(new NasaOptions { ApiKey = "test-key-0000" }),
            new CapturingLogger<RawImageFeedProvider>()), handler);

    [Theory]
    [InlineData("2017-02-27", 1621)]
    [InlineData("2018-06-02", 2069)]
    [InlineData("2016-07-13", 1398)]
    [Trait("AC", "AC-3")]
    [Trait("Decision", "D-1")]
    public async Task Each_sample_date_yields_at_least_three_photos_from_its_sol(string date, int sol)
    {
        var (provider, handler) = Build(RecordingHandler.Json(Fixture.Text($"rawfeed_sol{sol}.json")));

        var result = await provider.GetPhotosAsync("curiosity", DateOnly.Parse(date), 5, CancellationToken.None);

        var photos = Assert.IsType<ProviderResult.Photos>(result);
        Assert.Equal(5, photos.Selected.Count);
        Assert.All(photos.Selected, p => Assert.Equal(DateOnly.Parse(date), p.EarthDate));
        Assert.All(photos.Selected, p => Assert.Equal(sol, p.Sol));
        Assert.Equal("RawImageFeed", photos.Provider);
        Assert.Contains($"sol={sol}", handler.Requests.Single().Query);
    }

    [Fact]
    public async Task Requests_the_feed_without_any_key()
    {
        var (provider, handler) = Build(RecordingHandler.Json(Fixture.Text("rawfeed_sol1621.json")));

        await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var uri = handler.Requests.Single();
        Assert.Equal("mars.nasa.gov", uri.Host);
        Assert.Contains("feed=raw_images", uri.Query);
        Assert.Contains("category=msl", uri.Query);
        Assert.Contains("feedtype=json", uri.Query);
        Assert.DoesNotContain("api_key", uri.Query);
        Assert.DoesNotContain("test-key-0000", uri.ToString());
    }

    [Fact]
    public async Task Maps_fields_prefers_full_res_upgrades_to_https_and_sorts_by_image_id()
    {
        var (provider, _) = Build(RecordingHandler.Json(Fixture.Text("rawfeed_sol1621.json")));

        var result = (ProviderResult.Photos)await provider.GetPhotosAsync("curiosity", Day, 12, CancellationToken.None);

        var ids = result.Selected.Select(p => p.Id).ToList();
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal), ids);
        Assert.All(result.Selected, p =>
        {
            Assert.Equal("https", p.ImageUrl.Scheme);
            Assert.EndsWith("_DXXX.jpg", p.ImageUrl.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("-br", p.ImageUrl.AbsolutePath); // full_res, not the 'large' -br2 variant
            Assert.StartsWith("1621", p.Id);
            Assert.Equal("curiosity", p.Rover);
        });
        Assert.Equal(12, result.Available);
    }

    [Fact]
    public async Task Falls_back_to_the_large_image_when_full_res_is_missing()
    {
        const string body = """
            {"sol":1,"images":[{"imageid":"0001ML","sol":1,"camera":{"instrument":"MAST"},"image_files":{"large":"http://mars.nasa.gov/x/0001ML-br2.jpg"}}]}
            """;
        var (provider, _) = Build(RecordingHandler.Json(body));

        var result = (ProviderResult.Photos)await provider.GetPhotosAsync("curiosity", new DateOnly(2012, 8, 7), 5, CancellationToken.None);

        Assert.Equal("https://mars.nasa.gov/x/0001ML-br2.jpg", result.Selected[0].ImageUrl.ToString());
        Assert.Equal("MAST", result.Selected[0].Camera);
    }

    [Fact]
    public async Task Empty_image_list_is_no_photos()
    {
        var (provider, _) = Build(RecordingHandler.Json("""{"sol":1621,"images":[],"num_images":0}"""));

        Assert.IsType<ProviderResult.NoPhotos>(await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None));
    }

    [Theory]
    [InlineData("opportunity")]
    [InlineData("spirit")]
    public async Task Rovers_other_than_curiosity_are_unavailable_without_any_request(string rover)
    {
        var (provider, handler) = Build(RecordingHandler.Json("{}"));

        var result = await provider.GetPhotosAsync(rover, Day, 5, CancellationToken.None);

        var unavailable = Assert.IsType<ProviderResult.Unavailable>(result);
        Assert.Equal(ErrorCodes.ApiUnavailable, unavailable.ErrorCode);
        Assert.Contains("Curiosity only", unavailable.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("<html>maintenance</html>")]
    [InlineData("{ broken")]
    [InlineData("{\"sol\":1621}")]
    [InlineData("{\"images\":\"x\"}")]
    public async Task Malformed_responses_are_bad_responses(string body)
    {
        var (provider, _) = Build(RecordingHandler.Json(body));

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.Equal(ErrorCodes.ApiBadResponse, Assert.IsType<ProviderResult.Unavailable>(result).ErrorCode);
    }

    [Fact]
    public async Task Server_errors_are_unavailable()
    {
        var (provider, _) = Build(RecordingHandler.Html("down", HttpStatusCode.ServiceUnavailable));

        var result = await provider.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.Equal(ErrorCodes.ApiUnavailable, Assert.IsType<ProviderResult.Unavailable>(result).ErrorCode);
    }

    [Fact]
    public async Task Dates_before_landing_have_no_photos()
    {
        var (provider, handler) = Build(RecordingHandler.Json("{}"));

        var result = await provider.GetPhotosAsync("curiosity", new DateOnly(2001, 1, 1), 5, CancellationToken.None);

        Assert.IsType<ProviderResult.NoPhotos>(result);
        Assert.Empty(handler.Requests);
    }
}

public class ProviderChainTests
{
    private static readonly DateOnly Day = new(2017, 2, 27);

    private sealed class Stub(string name, ProviderResult result) : IPhotoProvider
    {
        public int Calls { get; private set; }

        public string Name => name;

        public Task<ProviderResult> GetPhotosAsync(string rover, DateOnly date, int maxPhotos, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static ProviderResult.Photos Found(string provider) =>
        new(FakeProviderPhotos(), 1) { Provider = provider };

    private static IReadOnlyList<RoverPhoto> FakeProviderPhotos() =>
        [new RoverPhoto("1", null, "C", new Uri("https://mars.nasa.gov/a.jpg"), Day, "curiosity")];

    private static ProviderResult.Unavailable Down(string message = "HTTP 404") => new(ErrorCodes.ApiUnavailable, message, false);

    [Fact]
    public async Task A_successful_primary_stops_the_chain()
    {
        var primary = new Stub("MarsRoverPhotos", Found("MarsRoverPhotos"));
        var fallback = new Stub("RawImageFeed", Found("RawImageFeed"));
        var chain = new ProviderChain([primary, fallback], new CapturingLogger<ProviderChain>());

        var result = await chain.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var photos = Assert.IsType<ProviderResult.Photos>(result);
        Assert.Equal("MarsRoverPhotos", photos.Provider);
        Assert.Null(photos.FallbackReason);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task No_photos_from_the_primary_is_final_and_does_not_fall_back()
    {
        var primary = new Stub("MarsRoverPhotos", new ProviderResult.NoPhotos { Provider = "MarsRoverPhotos" });
        var fallback = new Stub("RawImageFeed", Found("RawImageFeed"));
        var chain = new ProviderChain([primary, fallback], new CapturingLogger<ProviderChain>());

        var result = await chain.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.IsType<ProviderResult.NoPhotos>(result);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task An_unavailable_primary_falls_back_and_records_why()
    {
        var primary = new Stub("MarsRoverPhotos", Down("HTTP 404 (service not found)"));
        var fallback = new Stub("RawImageFeed", Found("RawImageFeed"));
        var log = new CapturingLogger<ProviderChain>();
        var chain = new ProviderChain([primary, fallback], log);

        var result = await chain.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var photos = Assert.IsType<ProviderResult.Photos>(result);
        Assert.Equal("RawImageFeed", photos.Provider);
        Assert.Contains("MarsRoverPhotos", photos.FallbackReason);
        Assert.Contains("HTTP 404", photos.FallbackReason);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);
        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("2017-02-27", warning.Message);
        Assert.Contains("HTTP 404", warning.Message);
    }

    [Fact]
    public async Task Both_sources_unavailable_is_an_unavailable_result_naming_both_reasons()
    {
        var chain = new ProviderChain(
            [new Stub("MarsRoverPhotos", Down("HTTP 404")), new Stub("RawImageFeed", Down("HTTP 503"))],
            new CapturingLogger<ProviderChain>());

        var result = await chain.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        var unavailable = Assert.IsType<ProviderResult.Unavailable>(result);
        Assert.Equal(ErrorCodes.ApiUnavailable, unavailable.ErrorCode);
        Assert.Contains("HTTP 404", unavailable.Message);
        Assert.Contains("HTTP 503", unavailable.Message);
        Assert.Contains("MarsRoverPhotos", unavailable.FallbackReason);
    }

    [Fact]
    public async Task No_photos_from_the_fallback_after_a_failed_primary_is_no_photos_with_the_reason()
    {
        var chain = new ProviderChain(
            [new Stub("MarsRoverPhotos", Down()), new Stub("RawImageFeed", new ProviderResult.NoPhotos { Provider = "RawImageFeed" })],
            new CapturingLogger<ProviderChain>());

        var result = await chain.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.IsType<ProviderResult.NoPhotos>(result);
        Assert.NotNull(result.FallbackReason);
    }

    [Fact]
    public async Task Provider_name_is_filled_in_when_a_provider_forgets_it()
    {
        var chain = new ProviderChain(
            [new Stub("Quiet", new ProviderResult.Photos(FakeProviderPhotos(), 1))],
            new CapturingLogger<ProviderChain>());

        var result = await chain.GetPhotosAsync("curiosity", Day, 5, CancellationToken.None);

        Assert.Equal("Quiet", result.Provider);
    }
}
