using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MarsPhotos.Tests.Providers;

internal static class Fixture
{
    public static string Text(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

internal sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return send(request, cancellationToken);
    }

    public static RecordingHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK, string? remaining = null) =>
        new((_, _) => Task.FromResult(Response(body, "application/json", status, remaining)));

    public static RecordingHandler Html(string body, HttpStatusCode status) =>
        new((_, _) => Task.FromResult(Response(body, "text/html", status)));

    public static HttpResponseMessage Response(string body, string contentType, HttpStatusCode status = HttpStatusCode.OK, string? remaining = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        if (remaining is not null)
        {
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", remaining);
        }

        return response;
    }
}

internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IEnumerable<string> Messages => Entries.Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : " " + exception)));
}
