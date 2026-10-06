using System.Net;
using System.Net.Http.Headers;
using MarsPhotos.Application;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Download;
using MarsPhotos.Infrastructure.Security;
using MarsPhotos.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Download;

public sealed class HttpPhotoDownloaderTests : IDisposable
{
    private static readonly DateOnly Day = new(2017, 2, 27);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mp-dl-" + Guid.NewGuid().ToString("N"));
    private readonly List<Uri> _requests = [];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FuncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private HttpPhotoDownloader Build(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send, Action<RunOptions>? configure = null)
    {
        var run = new RunOptions { OutputRoot = _root, DownloadTimeoutSeconds = 5 };
        configure?.Invoke(run);
        var handler = new FuncHandler((req, ct) =>
        {
            _requests.Add(req.RequestUri!);
            return send(req, ct);
        });
        return new HttpPhotoDownloader(
            new Factory(handler),
            new FileSystemPhotoStore(Options.Create(run)),
            new UrlPolicy(run.AllowedImageHosts),
            Options.Create(run),
            NullLogger<HttpPhotoDownloader>.Instance);
    }

    private static RoverPhoto Photo(string url = "https://mars.nasa.gov/msl-raw-images/a.jpg") =>
        new("123", 1621, "MARDI", new Uri(url), Day, "curiosity");

    private static HttpResponseMessage Image(int bytes = 4096, string contentType = "image/jpeg", HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(new byte[bytes]) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return response;
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(Func<HttpResponseMessage> make) =>
        (_, _) => Task.FromResult(make());

    private string[] Files() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];

    [Fact]
    [Trait("AC", "AC-4")]
    public async Task Downloads_an_image_into_the_store()
    {
        var downloader = Build(Respond(() => Image(4096)));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(4096, result.Bytes);
        Assert.Equal([Path.Combine(_root, "2017-02-27", "123.jpg")], Files());
    }

    [Fact]
    public async Task Upgrades_http_addresses_to_https_before_requesting()
    {
        var downloader = Build(Respond(() => Image()));

        await downloader.DownloadAsync(Photo("http://mars.nasa.gov/a.jpg"), Day, "123.jpg", CancellationToken.None);

        Assert.Equal("https", Assert.Single(_requests).Scheme);
    }

    [Fact]
    public async Task Rejected_hosts_are_not_requested_at_all()
    {
        var downloader = Build(Respond(() => Image()));

        var result = await downloader.DownloadAsync(Photo("https://evil.example.com/a.jpg"), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadRejectedUrl, result.ErrorCode);
        Assert.Empty(_requests);
        Assert.Empty(Files());
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData("application/octet-stream")]
    [Trait("AC", "AC-6")]
    public async Task Non_image_content_is_rejected_even_with_status_200(string contentType)
    {
        var downloader = Build(Respond(() => Image(500, contentType)));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadFailed, result.ErrorCode);
        Assert.Contains(contentType, result.Message);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task Zero_byte_responses_are_rejected()
    {
        var downloader = Build(Respond(() => Image(0)));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadFailed, result.ErrorCode);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task Oversized_images_are_rejected_by_declared_length()
    {
        var downloader = Build(Respond(() => Image(5000)), o => o.MaxImageBytes = 1000);

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadTooLarge, result.ErrorCode);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task Oversized_images_are_rejected_while_streaming_when_length_is_unknown()
    {
        var downloader = Build(Respond(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new UnknownLengthStream(5000)) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        }), o => o.MaxImageBytes = 1000);

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadTooLarge, result.ErrorCode);
        Assert.Empty(Files());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Http_errors_become_download_failures_with_the_status(HttpStatusCode status)
    {
        var downloader = Build(Respond(() => new HttpResponseMessage(status)));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadFailed, result.ErrorCode);
        Assert.Contains(((int)status).ToString(), result.Message);
        Assert.Empty(Files());
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task Network_errors_become_download_failures()
    {
        var downloader = Build((_, _) => throw new HttpRequestException("name not resolved"));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadFailed, result.ErrorCode);
        Assert.Empty(Files());
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task A_connection_dropped_mid_body_leaves_no_file()
    {
        var downloader = Build(Respond(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DroppingStream(2048)) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        }));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadFailed, result.ErrorCode);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task Follows_redirects_to_allowed_hosts_only()
    {
        var downloader = Build((req, _) => Task.FromResult(req.RequestUri!.Host == "mars.nasa.gov"
            ? Redirect("http://mars.jpl.nasa.gov/final.jpg")
            : Image()));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.Equal(["mars.nasa.gov", "mars.jpl.nasa.gov"], _requests.Select(r => r.Host));
        Assert.All(_requests, r => Assert.Equal("https", r.Scheme));
    }

    [Fact]
    public async Task A_redirect_to_an_untrusted_host_is_rejected()
    {
        var downloader = Build(Respond(() => Redirect("https://evil.example.com/steal.jpg")));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadRejectedUrl, result.ErrorCode);
        Assert.Single(_requests); // the evil host was never contacted
        Assert.Empty(Files());
    }

    [Fact]
    public async Task Too_many_redirects_fail()
    {
        var downloader = Build(Respond(() => Redirect("https://mars.nasa.gov/loop.jpg")));

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.DownloadFailed, result.ErrorCode);
        Assert.Contains("redirect", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(4, _requests.Count); // original + 3 redirects
    }

    [Fact]
    public async Task A_stalled_body_times_out_as_a_download_failure()
    {
        var downloader = Build(Respond(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        }), o => o.DownloadTimeoutSeconds = 1);

        var result = await downloader.DownloadAsync(Photo(), Day, "123.jpg", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var downloader = Build(Respond(() => Image()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => downloader.DownloadAsync(Photo(), Day, "123.jpg", cts.Token));
    }

    [Fact]
    public async Task Cancellation_while_the_body_is_downloading_propagates_and_leaves_no_file()
    {
        using var cts = new CancellationTokenSource();
        var downloader = Build(Respond(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return response;
        }));

        var task = downloader.DownloadAsync(Photo(), Day, "123.jpg", cts.Token);
        await Task.Delay(150); // the request is now stalled inside the body read
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(Files());
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private sealed class UnknownLengthStream(int total) : Stream
    {
        private int _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, total - _served);
            _served += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DroppingStream(int before) : Stream
    {
        private int _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_served >= before)
            {
                throw new IOException("The connection was reset.");
            }

            var n = Math.Min(count, before - _served);
            _served += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
