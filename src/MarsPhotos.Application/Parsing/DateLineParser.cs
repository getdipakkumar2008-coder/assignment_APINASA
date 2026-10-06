using System.Globalization;
using System.Text.RegularExpressions;
using MarsPhotos.Domain;

namespace MarsPhotos.Application.Parsing;

/// <summary>
/// Interprets one input line as a calendar date (FR-003 to FR-006). Uses explicit formats and the invariant
/// culture so results never depend on the machine's regional settings; two-digit years mean 2000 to 2099.
/// Never throws for bad input: every outcome is a <see cref="ParsedDate"/>.
/// </summary>
public static partial class DateLineParser
{
    public const int DefaultMaxLineLength = 200;

    public const string SupportedFormatsText =
        "MM/dd/yy, MM/dd/yyyy, MMMM d, yyyy, MMM d, yyyy, MMM-dd-yyyy, d MMMM yyyy, yyyy-MM-dd";

    private static readonly string[] Formats =
    [
        "MM/dd/yy",
        "M/d/yy",
        "MM/dd/yyyy",
        "M/d/yyyy",
        "MMMM d, yyyy",
        "MMM d, yyyy",
        "MMM-dd-yyyy",
        "MMM-d-yyyy",
        "MMMM-d-yyyy",
        "MMMM-dd-yyyy",
        "yyyy-MM-dd",
        "yyyy-M-d",
        "d MMMM yyyy",
        "d MMM yyyy",
    ];

    private static readonly CultureInfo Culture = CreateCulture();

    private static CultureInfo CreateCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar { TwoDigitYearMax = 2099 };
        return culture;
    }

    public static ParsedDate Parse(DateInputLine line, int maxLineLength = DefaultMaxLineLength)
    {
        if (line.RejectReason is not null)
        {
            return ParsedDate.Unrecognized(line, line.RejectReason);
        }

        var text = line.RawText ?? string.Empty;
        if (text.Length > maxLineLength)
        {
            return ParsedDate.Unrecognized(line, $"Line is longer than {maxLineLength} characters.");
        }

        var normalized = WhitespaceRun().Replace(text.Trim(), " ");
        if (normalized.Length == 0)
        {
            return ParsedDate.Unrecognized(line, "Line is empty.");
        }

        if (DateOnly.TryParseExact(normalized, Formats, Culture, DateTimeStyles.None, out var date))
        {
            return ParsedDate.Valid(line, date);
        }

        return Diagnose(line, normalized);
    }

    /// <summary>The text matched a known shape but is not a real date, or it is not a date at all.</summary>
    private static ParsedDate Diagnose(DateInputLine line, string text)
    {
        var numeric = NumericShape().Match(text);
        if (numeric.Success)
        {
            var year = int.Parse(numeric.Groups[3].ValueSpan, CultureInfo.InvariantCulture);
            if (numeric.Groups[3].Length == 2)
            {
                year += 2000;
            }

            return Check(line, year, int.Parse(numeric.Groups[1].ValueSpan, CultureInfo.InvariantCulture),
                int.Parse(numeric.Groups[2].ValueSpan, CultureInfo.InvariantCulture), slashDate: true);
        }

        var iso = IsoShape().Match(text);
        if (iso.Success)
        {
            return Check(line, int.Parse(iso.Groups[1].ValueSpan, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[2].ValueSpan, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups[3].ValueSpan, CultureInfo.InvariantCulture), slashDate: false);
        }

        var named = MonthFirstShape().Match(text);
        var dayFirst = DayFirstShape().Match(text);
        if (named.Success || dayFirst.Success)
        {
            var monthText = named.Success ? named.Groups[1].Value : dayFirst.Groups[2].Value;
            var dayText = named.Success ? named.Groups[2].Value : dayFirst.Groups[1].Value;
            var yearText = named.Success ? named.Groups[3].Value : dayFirst.Groups[3].Value;
            var month = MonthNumber(monthText);
            if (month > 0)
            {
                return Check(line, int.Parse(yearText, CultureInfo.InvariantCulture), month,
                    int.Parse(dayText, CultureInfo.InvariantCulture), slashDate: false);
            }
        }

        return ParsedDate.Unrecognized(
            line,
            $"'{text}' is not a recognized date. Supported formats: {SupportedFormatsText}.");
    }

    private static ParsedDate Check(DateInputLine line, int year, int month, int day, bool slashDate)
    {
        if (year is < 1 or > 9999)
        {
            return ParsedDate.Invalid(line, $"Year {year} is not valid");
        }

        if (month is < 1 or > 12)
        {
            return ParsedDate.Invalid(
                line,
                slashDate
                    ? $"Month {month} is not valid (slash dates are read month-first: MM/dd/yy)"
                    : $"Month {month} is not valid");
        }

        if (day is < 1 or > 31)
        {
            return ParsedDate.Invalid(line, $"Day {day} is not valid");
        }

        if (day > DateTime.DaysInMonth(year, month))
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
            return ParsedDate.Invalid(line, $"Day {day} does not exist in {monthName} {year}");
        }

        // The shape and ranges are valid yet no supported format matched (should not happen); treat as unrecognized.
        return ParsedDate.Unrecognized(
            line,
            $"'{line.RawText}' is not a recognized date. Supported formats: {SupportedFormatsText}.");
    }

    private static int MonthNumber(string name)
    {
        var info = CultureInfo.InvariantCulture.DateTimeFormat;
        for (var m = 1; m <= 12; m++)
        {
            if (string.Equals(info.GetMonthName(m), name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(info.GetAbbreviatedMonthName(m), name, StringComparison.OrdinalIgnoreCase))
            {
                return m;
            }
        }

        return 0;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"^(\d{1,2})/(\d{1,2})/(\d{2}|\d{4})$")]
    private static partial Regex NumericShape();

    [GeneratedRegex(@"^(\d{4})-(\d{1,2})-(\d{1,2})$")]
    private static partial Regex IsoShape();

    [GeneratedRegex(@"^([A-Za-z]{3,9})\.?[ -](\d{1,2}),?[ -](\d{4})$")]
    private static partial Regex MonthFirstShape();

    [GeneratedRegex(@"^(\d{1,2}) ([A-Za-z]{3,9}) (\d{4})$")]
    private static partial Regex DayFirstShape();
}
