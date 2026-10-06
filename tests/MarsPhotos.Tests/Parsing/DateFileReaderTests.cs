using System.Text;
using MarsPhotos.Application;
using MarsPhotos.Domain;
using MarsPhotos.Infrastructure.Input;
using Microsoft.Extensions.Options;

namespace MarsPhotos.Tests.Parsing;

public sealed class DateFileReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mp-dates-" + Guid.NewGuid().ToString("N"));

    public DateFileReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_dir, "dates.txt");
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    private static FileDateSource Reader(RunOptions? options = null) => new(Options.Create(options ?? new RunOptions()));

    [Fact]
    [Trait("AC", "AC-2")]
    public async Task Reads_the_sample_lines_trimmed_and_in_order()
    {
        var path = Write("02/27/17  \nJune 2, 2018  \nJul-13-2016\nApril 31, 2018\n");

        var lines = await Reader().ReadAsync(path, CancellationToken.None);

        Assert.Equal(["02/27/17", "June 2, 2018", "Jul-13-2016", "April 31, 2018"], lines.Select(l => l.RawText));
        Assert.Equal([1, 2, 3, 4], lines.Select(l => l.LineNumber));
        Assert.All(lines, l => Assert.Null(l.RejectReason));
    }

    [Fact]
    public async Task Skips_blank_and_comment_lines_but_keeps_physical_line_numbers()
    {
        var path = Write("\n02/27/17\n   \n# a comment\nJune 2, 2018\n");

        var lines = await Reader().ReadAsync(path, CancellationToken.None);

        Assert.Equal(["02/27/17", "June 2, 2018"], lines.Select(l => l.RawText));
        Assert.Equal([2, 5], lines.Select(l => l.LineNumber));
    }

    [Theory]
    [InlineData("a\r\nb\r\n")]
    [InlineData("a\nb\n")]
    [InlineData("a\rb\r")]
    public async Task Handles_all_line_ending_styles(string content)
    {
        var lines = await Reader().ReadAsync(Write(content), CancellationToken.None);

        Assert.Equal(["a", "b"], lines.Select(l => l.RawText));
    }

    [Fact]
    public async Task Handles_a_utf8_byte_order_mark()
    {
        var path = Write("02/27/17\nJune 2, 2018\n", new UTF8Encoding(true));

        var lines = await Reader().ReadAsync(path, CancellationToken.None);

        Assert.Equal("02/27/17", lines[0].RawText);
    }

    [Fact]
    [Trait("AC", "AC-6")]
    public async Task Missing_file_is_a_fatal_input_error()
    {
        var ex = await Assert.ThrowsAsync<FatalRunException>(
            () => Reader().ReadAsync(Path.Combine(_dir, "nope.txt"), CancellationToken.None));

        Assert.Equal(ErrorCodes.InputFileError, ex.ErrorCode);
        Assert.Contains("nope.txt", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  \n")]
    [InlineData("# only comments\n")]
    public async Task Empty_file_is_a_fatal_input_error(string content)
    {
        var ex = await Assert.ThrowsAsync<FatalRunException>(
            () => Reader().ReadAsync(Write(content), CancellationToken.None));

        Assert.Equal(ErrorCodes.InputFileError, ex.ErrorCode);
        Assert.Contains("no dates", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_directory_instead_of_a_file_is_a_fatal_input_error()
    {
        var ex = await Assert.ThrowsAsync<FatalRunException>(() => Reader().ReadAsync(_dir, CancellationToken.None));

        Assert.Equal(ErrorCodes.InputFileError, ex.ErrorCode);
    }

    [Fact]
    public async Task Lines_beyond_the_limit_are_kept_but_flagged_not_dropped()
    {
        var path = Write(string.Join('\n', Enumerable.Range(1, 5).Select(i => $"01/0{i}/17")));

        var lines = await Reader(new RunOptions { MaxFileLines = 3 }).ReadAsync(path, CancellationToken.None);

        Assert.Equal(5, lines.Count);
        Assert.All(lines.Take(3), l => Assert.Null(l.RejectReason));
        Assert.All(lines.Skip(3), l => Assert.Contains("3", l.RejectReason));
    }

    [Fact]
    public async Task Overlong_lines_are_truncated_and_flagged()
    {
        var path = Write(new string('x', 500));

        var lines = await Reader().ReadAsync(path, CancellationToken.None);

        Assert.Single(lines);
        Assert.Equal(200, lines[0].RawText.Length);
        Assert.Contains("200", lines[0].RejectReason);
    }

    [Fact]
    public async Task Cancellation_is_honored()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Reader().ReadAsync(Write("02/27/17\n"), cts.Token));
    }
}
