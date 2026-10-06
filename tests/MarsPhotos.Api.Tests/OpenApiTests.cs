using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MarsPhotos.Api.Tests;

public class OpenApiTests
{
    [Fact]
    public async Task The_contract_is_served_as_yaml_and_is_the_spec_file()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi.yaml");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/yaml", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("openapi: 3.0.3", body);
        Assert.Contains("/api/runs", body);
        Assert.Equal(await File.ReadAllTextAsync(SpecFile()), body); // one source of truth
    }

    [Fact]
    public async Task Swagger_ui_is_available_and_points_at_the_contract()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var index = await client.GetAsync("/swagger/index.html");
        var html = await index.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("swagger-ui", html);
        var config = await (await client.GetAsync("/swagger/index.js")).Content.ReadAsStringAsync();
        Assert.Contains("openapi.yaml", config); // the UI is configured to load our contract
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger")).StatusCode); // redirects to the index
    }

    [Fact]
    public async Task Documentation_endpoints_never_contain_the_key()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var yaml = await (await client.GetAsync("/openapi.yaml")).Content.ReadAsStringAsync();
        var ui = await (await client.GetAsync("/swagger/index.html")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(TestKey.Value, yaml + ui);
    }

    /// <summary>Drift guard: every real API route is documented, and every documented route really exists.</summary>
    [Fact]
    public void Every_api_endpoint_is_in_the_contract_and_every_contract_operation_exists()
    {
        using var factory = new ApiFactory();
        _ = factory.CreateClient();
        var actual = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []).Select(m => (Method: m.ToLowerInvariant(), Path: e.RoutePattern.RawText ?? string.Empty)))
            .Where(x => x.Path.StartsWith("/api/", StringComparison.Ordinal) || x.Path == "/health")
            .ToHashSet();
        var documented = DocumentedOperations();

        Assert.NotEmpty(actual);
        var undocumented = actual.Except(documented).Select(x => $"{x.Method} {x.Path}").ToList();
        Assert.True(undocumented.Count == 0, "In the API but not in openapi.yaml: " + string.Join(", ", undocumented));

        // /photos/{date}/{file} is served by the static-file middleware, so it is not a routed endpoint.
        var phantom = documented.Except(actual).Where(x => !x.Path.StartsWith("/photos/", StringComparison.Ordinal))
            .Select(x => $"{x.Method} {x.Path}").ToList();
        Assert.True(phantom.Count == 0, "In openapi.yaml but not in the API: " + string.Join(", ", phantom));
    }

    private static string SpecFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MarsPhotos.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "specs", "001-mars-photo-downloader", "contracts", "openapi.yaml");
    }

    private static HashSet<(string Method, string Path)> DocumentedOperations()
    {
        var result = new HashSet<(string, string)>();
        string? path = null;
        var inPaths = false;
        foreach (var line in File.ReadAllLines(SpecFile()))
        {
            if (line.StartsWith("paths:", StringComparison.Ordinal))
            {
                inPaths = true;
                continue;
            }

            if (inPaths && line.Length > 0 && line[0] != ' ')
            {
                break; // next top-level section (components)
            }

            var p = Regex.Match(line, @"^  (/[^:\s]*):\s*$");
            if (p.Success)
            {
                path = p.Groups[1].Value;
                continue;
            }

            var m = Regex.Match(line, @"^    (get|post|put|delete|patch):\s*$");
            if (inPaths && path is not null && m.Success)
            {
                result.Add((m.Groups[1].Value, path));
            }
        }

        return result;
    }
}
