using MarsPhotos.Infrastructure.Security;

namespace MarsPhotos.Tests.Security;

public class UrlPolicyTests
{
    private static readonly UrlPolicy Policy = new(["mars.nasa.gov", "mars.jpl.nasa.gov"]);

    [Theory]
    [InlineData("https://mars.nasa.gov/msl-raw-images/a.jpg", "https://mars.nasa.gov/msl-raw-images/a.jpg")]
    [InlineData("https://mars.jpl.nasa.gov/msl-raw-images/a.jpg", "https://mars.jpl.nasa.gov/msl-raw-images/a.jpg")]
    [InlineData("http://mars.nasa.gov/msl-raw-images/a.jpg", "https://mars.nasa.gov/msl-raw-images/a.jpg")] // http upgraded
    [InlineData("HTTPS://MARS.NASA.GOV/x.jpg", "https://mars.nasa.gov/x.jpg")]
    [Trait("AC", "AC-6")]
    public void Accepts_allow_listed_hosts_and_upgrades_http(string input, string expected)
    {
        var result = Policy.Evaluate(input);

        Assert.True(result.IsAllowed, result.Reason);
        Assert.Equal(expected, result.Url!.ToString());
    }

    [Theory]
    [InlineData("https://evil.com/a.jpg")]
    [InlineData("https://mars.nasa.gov.evil.com/a.jpg")]
    [InlineData("https://evilmars.nasa.gov/a.jpg")]
    [InlineData("https://nasa.gov/a.jpg")]
    [InlineData("https://sub.mars.nasa.gov/a.jpg")]
    [InlineData("https://user@mars.nasa.gov/a.jpg")]
    [InlineData("https://user:pw@mars.nasa.gov/a.jpg")]
    [InlineData("https://mars.nasa.gov:8443/a.jpg")]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("ftp://mars.nasa.gov/a.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path.jpg")]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_everything_else(string input)
    {
        var result = Policy.Evaluate(input);

        Assert.False(result.IsAllowed);
        Assert.Null(result.Url);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void Default_port_443_is_allowed_explicitly()
    {
        Assert.True(Policy.Evaluate("https://mars.nasa.gov:443/a.jpg").IsAllowed);
    }
}
