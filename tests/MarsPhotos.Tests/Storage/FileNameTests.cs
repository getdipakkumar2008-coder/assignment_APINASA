using MarsPhotos.Application;

namespace MarsPhotos.Tests.Storage;

public class FileNameTests
{
    [Theory]
    [InlineData("1621MD0006280000200678E01_DXXX", "1621MD0006280000200678E01_DXXX")]
    [InlineData("102693", "102693")]
    [InlineData("a b/c\\d:e*f?g\"h<i>j|k", "a_b_c_d_e_f_g_h_i_j_k")]
    [InlineData("../../etc/passwd", ".._.._etc_passwd")]
    [InlineData("C:\\Windows\\system32", "C__Windows_system32")]
    [InlineData("é日本", "___")]
    public void Sanitize_replaces_anything_outside_the_safe_set(string input, string expected)
    {
        Assert.Equal(expected, FileNames.Sanitize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    public void Sanitize_never_returns_an_empty_or_dot_only_stem(string input)
    {
        var stem = FileNames.Sanitize(input);

        Assert.False(string.IsNullOrWhiteSpace(stem));
        Assert.False(stem.All(c => c == '.'));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("lpt9")]
    public void Sanitize_neutralizes_windows_reserved_names(string input)
    {
        var stem = FileNames.Sanitize(input);

        Assert.False(string.Equals(input, stem, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sanitize_truncates_long_stems_to_100_characters()
    {
        Assert.Equal(100, FileNames.Sanitize(new string('a', 500)).Length);
    }

    [Theory]
    [InlineData("https://mars.nasa.gov/x/y/a.JPG", ".jpg")]
    [InlineData("https://mars.nasa.gov/x/y/a.jpeg", ".jpeg")]
    [InlineData("https://mars.nasa.gov/x/y/a.png", ".png")]
    [InlineData("https://mars.nasa.gov/x/y/a.exe", ".jpg")]
    [InlineData("https://mars.nasa.gov/x/y/a", ".jpg")]
    [InlineData("https://mars.nasa.gov/x/y/a.jpg?x=1", ".jpg")]
    public void ExtensionFor_uses_an_allow_list_and_defaults_to_jpg(string url, string expected)
    {
        Assert.Equal(expected, FileNames.ExtensionFor(new Uri(url)));
    }

    [Fact]
    public void EnsureInside_returns_the_full_path_for_safe_relative_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "mp-root");

        var full = FileNames.EnsureInside(root, Path.Combine("2017-02-27", "a.jpg"));

        Assert.StartsWith(Path.GetFullPath(root), full, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("2017-02-27/../../outside.jpg")]
    [InlineData("..\\outside.jpg")]
    public void EnsureInside_rejects_traversal(string relative)
    {
        var root = Path.Combine(Path.GetTempPath(), "mp-root");

        Assert.Throws<ArgumentException>(() => FileNames.EnsureInside(root, relative));
    }

    [Fact]
    public void EnsureInside_rejects_absolute_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "mp-root");
        var absolute = Path.Combine(Path.GetTempPath(), "other", "x.jpg");

        Assert.Throws<ArgumentException>(() => FileNames.EnsureInside(root, absolute));
    }

    [Fact]
    public void EnsureInside_does_not_treat_a_sibling_with_the_same_prefix_as_inside()
    {
        var root = Path.Combine(Path.GetTempPath(), "mp-root");

        Assert.Throws<ArgumentException>(() => FileNames.EnsureInside(root, "../mp-root-evil/x.jpg"));
    }
}
