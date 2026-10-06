namespace MarsPhotos.Infrastructure.Security;

/// <summary>Outcome of <see cref="UrlPolicy.Evaluate"/>.</summary>
public sealed record UrlCheck(Uri? Url, string? Reason)
{
    public bool IsAllowed => Url is not null;
}

/// <summary>
/// Decides whether a photo address may be fetched: HTTPS only (HTTP is upgraded), exact allow-listed host,
/// no credentials in the address, default port only (FR-018, FR-034, SEC-4).
/// </summary>
public sealed class UrlPolicy(IEnumerable<string> allowedHosts)
{
    private readonly HashSet<string> _hosts = new(allowedHosts, StringComparer.OrdinalIgnoreCase);

    public UrlCheck Evaluate(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
        {
            return Reject("address is empty or not an absolute URL");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return Reject($"scheme '{uri.Scheme}' is not allowed");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Reject("credentials in the address are not allowed");
        }

        if (!uri.IsDefaultPort)
        {
            return Reject($"port {uri.Port} is not allowed");
        }

        if (!_hosts.Contains(uri.Host))
        {
            return Reject($"host '{uri.Host}' is not on the allow-list");
        }

        var upgraded = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
        return new UrlCheck(upgraded, null);
    }

    private static UrlCheck Reject(string reason) => new(null, reason);
}
