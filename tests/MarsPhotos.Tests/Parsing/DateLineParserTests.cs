using System.Globalization;
using MarsPhotos.Application.Parsing;
using MarsPhotos.Domain;

namespace MarsPhotos.Tests.Parsing;

public class DateLineParserTests
{
    private static ParsedDate Parse(string text) => DateLineParser.Parse(new DateInputLine(1, text));

    [Theory]
    [InlineData("02/27/17", "2017-02-27")]
    [InlineData("June 2, 2018", "2018-06-02")]
    [InlineData("Jul-13-2016", "2016-07-13")]
    [InlineData("2/27/17", "2017-02-27")]
    [InlineData("02/27/2017", "2017-02-27")]
    [InlineData("2/7/2017", "2017-02-07")]
    [InlineData("2017-02-27", "2017-02-27")]
    [InlineData("Feb 27, 2017", "2017-02-27")]
    [InlineData("February 27, 2017", "2017-02-27")]
    [InlineData("27 February 2017", "2017-02-27")]
    [InlineData("February-27-2017", "2017-02-27")]
    [InlineData("jul-13-2016", "2016-07-13")]
    [InlineData("JUNE 2, 2018", "2018-06-02")]
    [InlineData("  June 2, 2018  ", "2018-06-02")]
    [InlineData("June   2,   2018", "2018-06-02")]
    [InlineData("2/29/2016", "2016-02-29")]
    [InlineData("12/31/99", "2099-12-31")]
    [InlineData("01/01/00", "2000-01-01")]
    [Trait("AC", "AC-2")]
    public void Parses_supported_formats(string text, string expected)
    {
        var result = Parse(text);

        Assert.Equal(DateStatus.Valid, result.Status);
        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), result.Value);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    [Trait("AC", "AC-2")]
    public void April_31_is_invalid_with_a_clear_reason()
    {
        var result = Parse("April 31, 2018");

        Assert.Equal(DateStatus.Invalid, result.Status);
        Assert.Null(result.Value);
        Assert.Equal(ErrorCodes.DateInvalid, result.ErrorCode);
        Assert.Equal("Day 31 does not exist in April 2018", result.Message);
    }

    [Theory]
    [InlineData("2/29/2017", "Day 29 does not exist in February 2017")]
    [InlineData("02/30/2016", "Day 30 does not exist in February 2016")]
    [InlineData("Sep-31-2016", "Day 31 does not exist in September 2016")]
    [InlineData("2018-06-31", "Day 31 does not exist in June 2018")]
    [InlineData("31 June 2018", "Day 31 does not exist in June 2018")]
    [InlineData("13/01/17", "Month 13 is not valid (slash dates are read month-first: MM/dd/yy)")]
    [InlineData("00/10/17", "Month 0 is not valid (slash dates are read month-first: MM/dd/yy)")]
    [InlineData("06/00/17", "Day 0 is not valid")]
    [InlineData("06/32/17", "Day 32 is not valid")]
    [InlineData("2018-13-01", "Month 13 is not valid")]
    public void Calendar_impossible_dates_are_invalid_never_rolled_over(string text, string message)
    {
        var result = Parse(text);

        Assert.Equal(DateStatus.Invalid, result.Status);
        Assert.Equal(ErrorCodes.DateInvalid, result.ErrorCode);
        Assert.Equal(message, result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("tomorrow")]
    [InlineData("99/99/99x")]
    [InlineData("June 2018")]
    [InlineData("Smarch 2, 2018")]
    [InlineData("2018/06/02")]
    [InlineData("6-2-2018")]
    [InlineData("June 2nd, 2018")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("02/27/17 extra")]
    public void Unsupported_text_is_unrecognized_without_exceptions(string text)
    {
        var result = Parse(text);

        Assert.Equal(DateStatus.Unrecognized, result.Status);
        Assert.Equal(ErrorCodes.DateUnrecognized, result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Unrecognized_message_lists_the_supported_formats()
    {
        var message = Parse("abc").Message!;

        Assert.Contains("MM/dd/yy", message);
        Assert.Contains("MMMM d, yyyy", message);
        Assert.Contains("MMM-dd-yyyy", message);
    }

    [Fact]
    public void Overlong_lines_are_unrecognized_with_a_limit_message()
    {
        var result = DateLineParser.Parse(new DateInputLine(1, new string('9', 201)), maxLineLength: 200);

        Assert.Equal(DateStatus.Unrecognized, result.Status);
        Assert.Contains("200", result.Message);
    }

    [Fact]
    public void Lines_rejected_by_the_reader_are_reported_with_the_reason()
    {
        var result = DateLineParser.Parse(new DateInputLine(1001, "June 2, 2018", RejectReason: "Input limit of 1000 lines exceeded"));

        Assert.Equal(DateStatus.Unrecognized, result.Status);
        Assert.Equal("Input limit of 1000 lines exceeded", result.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ja-JP")]
    [InlineData("tr-TR")]
    [Trait("AC", "AC-2")]
    public void Results_do_not_depend_on_the_machine_culture(string cultureName)
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

            Assert.Equal(new DateOnly(2017, 2, 27), Parse("02/27/17").Value);
            Assert.Equal(new DateOnly(2018, 6, 2), Parse("June 2, 2018").Value);
            Assert.Equal(new DateOnly(2016, 7, 13), Parse("Jul-13-2016").Value);
            Assert.Equal(DateStatus.Invalid, Parse("April 31, 2018").Status);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }

    [Fact]
    public void Original_text_is_preserved_on_every_result()
    {
        Assert.Equal("April 31, 2018", Parse("April 31, 2018").Line.RawText);
        Assert.Equal("abc", Parse("abc").Line.RawText);
    }
}
