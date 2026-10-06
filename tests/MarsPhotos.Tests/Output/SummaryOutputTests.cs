using System.Text.Json;
using MarsPhotos.Application.Output;
using MarsPhotos.Domain;

namespace MarsPhotos.Tests.Output;

internal static class SampleSummaries
{
    public static readonly DateOnly D1 = new(2017, 2, 27);

    public static RunSummary Mixed()
    {
        var ok = new DateResult(
            "02/27/17", D1, DateResultStatus.Success, "RawImageFeed", "primary returned HTTP 404", 20,
            [
                new PhotoOutcome("a1", PhotoStatus.Downloaded, "2017-02-27/a1.jpg", null, null, 100),
                new PhotoOutcome("a2", PhotoStatus.Downloaded, "2017-02-27/a2.jpg", null, null, 100),
                new PhotoOutcome("a3", PhotoStatus.Skipped, "2017-02-27/a3.jpg", null, null, null),
            ],
            []);
        var partial = new DateResult(
            "June 2, 2018", new DateOnly(2018, 6, 2), DateResultStatus.PartialFailure, "RawImageFeed", null, 5,
            [
                new PhotoOutcome("b1", PhotoStatus.Downloaded, "2018-06-02/b1.jpg", null, null, 50),
                new PhotoOutcome("b2", PhotoStatus.Failed, null, ErrorCodes.DownloadFailed, "HTTP 500 for https://x/y?api_key=test-key-0000", null),
            ],
            [new ErrorInfo(ErrorCodes.DownloadFailed, "b2: HTTP 500 for https://x/y?api_key=test-key-0000", "api_key=test-key-0000")]);
        var invalid = DateResult.WithoutLookup(
            "April 31, 2018", null, DateResultStatus.InvalidDate,
            new ErrorInfo(ErrorCodes.DateInvalid, "Day 31 does not exist in April 2018"));
        var outside = DateResult.WithoutLookup(
            "01/01/01", new DateOnly(2001, 1, 1), DateResultStatus.OutsideMissionRange,
            note: "Outside the curiosity mission range (2012-08-06 to 2026-10-06); no lookup made.");
        DateResult[] results = [ok, partial, invalid, outside];
        return new RunSummary("curiosity", new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromMilliseconds(8421), RunTotals.From(results), results);
    }
}

public class ConsoleSummaryRendererTests
{
    [Fact]
    [Trait("AC", "AC-7")]
    public void Lists_every_input_line_once_with_its_date_counts_and_errors()
    {
        var text = ConsoleSummaryRenderer.Render(SampleSummaries.Mixed());
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        Assert.Contains("rover: curiosity", lines[0]);
        Assert.Contains("Input", lines[1]);
        foreach (var input in new[] { "02/27/17", "June 2, 2018", "April 31, 2018", "01/01/01" })
        {
            Assert.Single(lines, l => l.StartsWith(input, StringComparison.Ordinal));
        }

        var ok = lines.Single(l => l.StartsWith("02/27/17", StringComparison.Ordinal));
        Assert.Contains("2017-02-27", ok);
        Assert.Contains("Success", ok);
        Assert.Contains("RawImageFeed", ok);
        Assert.Contains("fallback: primary returned HTTP 404", ok);

        var invalid = lines.Single(l => l.StartsWith("April 31, 2018", StringComparison.Ordinal));
        Assert.Contains("InvalidDate", invalid);
        Assert.Contains("DATE_INVALID: Day 31 does not exist in April 2018", invalid);

        var outside = lines.Single(l => l.StartsWith("01/01/01", StringComparison.Ordinal));
        Assert.Contains("OutsideMissionRange", outside);
        Assert.Contains("mission range", outside);
    }

    [Fact]
    public void Counts_columns_show_new_skipped_and_failed_photos()
    {
        var text = ConsoleSummaryRenderer.Render(SampleSummaries.Mixed());
        var ok = text.Split('\n').Single(l => l.StartsWith("02/27/17", StringComparison.Ordinal));
        var cells = ok.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // ... Status Source New Skip Fail ...
        var statusIndex = Array.IndexOf(cells, "Success");
        Assert.Equal(["2", "1", "0"], cells.Skip(statusIndex + 2).Take(3));
    }

    [Fact]
    [Trait("AC", "AC-7")]
    public void Totals_equal_the_sum_of_the_lines()
    {
        var text = ConsoleSummaryRenderer.Render(SampleSummaries.Mixed());

        var totals = text.Split('\n').Last(l => l.StartsWith("Totals:", StringComparison.Ordinal));
        Assert.Equal("Totals: 4 lines | 3 valid | 1 invalid | 1 outside range | 3 downloaded | 1 skipped | 1 failed", totals.TrimEnd('\r'));
    }

    [Fact]
    [Trait("AC", "AC-8")]
    public void Secrets_never_appear_in_the_output()
    {
        var text = ConsoleSummaryRenderer.Render(SampleSummaries.Mixed());

        Assert.DoesNotContain("test-key-0000", text);
        Assert.Contains("api_key=***", text);
    }

    [Fact]
    public void Renders_an_empty_summary_without_throwing()
    {
        var summary = new RunSummary("curiosity", DateTimeOffset.UnixEpoch, TimeSpan.Zero, RunTotals.From([]), []);

        var text = ConsoleSummaryRenderer.Render(summary);

        Assert.Contains("Totals: 0 lines", text);
    }
}

public class RunSummaryJsonTests
{
    private static JsonElement Serialize(RunSummary summary) => JsonDocument.Parse(RunSummaryJson.Serialize(summary)).RootElement;

    [Fact]
    public void Matches_the_openapi_run_summary_shape()
    {
        var root = Serialize(SampleSummaries.Mixed());

        Assert.Equal("curiosity", root.GetProperty("rover").GetString());
        Assert.Equal(8421, root.GetProperty("durationMs").GetInt32());
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T12:00:00Z"), root.GetProperty("startedAt").GetDateTimeOffset());

        var totals = root.GetProperty("totals");
        Assert.Equal(4, totals.GetProperty("lines").GetInt32());
        Assert.Equal(3, totals.GetProperty("valid").GetInt32());
        Assert.Equal(1, totals.GetProperty("invalid").GetInt32());
        Assert.Equal(1, totals.GetProperty("outsideMissionRange").GetInt32());
        Assert.Equal(3, totals.GetProperty("downloaded").GetInt32());
        Assert.Equal(1, totals.GetProperty("skipped").GetInt32());
        Assert.Equal(1, totals.GetProperty("failed").GetInt32());

        Assert.Equal(4, root.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public void Dates_are_iso_strings_and_null_when_invalid_and_statuses_are_names()
    {
        var results = Serialize(SampleSummaries.Mixed()).GetProperty("results");

        Assert.Equal("2017-02-27", results[0].GetProperty("date").GetString());
        Assert.Equal("Success", results[0].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, results[2].GetProperty("date").ValueKind);
        Assert.Equal("InvalidDate", results[2].GetProperty("status").GetString());
        Assert.Equal("OutsideMissionRange", results[3].GetProperty("status").GetString());
    }

    [Fact]
    public void Photos_carry_relative_urls_statuses_and_errors()
    {
        var results = Serialize(SampleSummaries.Mixed()).GetProperty("results");
        var photos = results[0].GetProperty("photos");

        Assert.Equal("/photos/2017-02-27/a1.jpg", photos[0].GetProperty("url").GetString());
        Assert.Equal("Downloaded", photos[0].GetProperty("status").GetString());
        Assert.Equal("Skipped", photos[2].GetProperty("status").GetString());
        Assert.Equal(20, results[0].GetProperty("photosAvailable").GetInt32());

        var failed = results[1].GetProperty("photos")[1];
        Assert.Equal(JsonValueKind.Null, failed.GetProperty("url").ValueKind);
        Assert.Equal("DL_FAILED", failed.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Counts_provider_fallback_reason_and_notes_are_exposed()
    {
        var first = Serialize(SampleSummaries.Mixed()).GetProperty("results")[0];

        Assert.Equal(2, first.GetProperty("downloaded").GetInt32());
        Assert.Equal(1, first.GetProperty("skipped").GetInt32());
        Assert.Equal(0, first.GetProperty("failed").GetInt32());
        Assert.Equal("RawImageFeed", first.GetProperty("provider").GetString());
        Assert.Equal("primary returned HTTP 404", first.GetProperty("fallbackReason").GetString());
    }

    [Fact]
    [Trait("AC", "AC-8")]
    public void Secrets_and_error_details_never_appear_in_the_json()
    {
        var json = RunSummaryJson.Serialize(SampleSummaries.Mixed());

        Assert.DoesNotContain("test-key-0000", json);
        Assert.DoesNotContain("detail", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Output_is_camel_case()
    {
        var json = RunSummaryJson.Serialize(SampleSummaries.Mixed());

        Assert.Contains("\"photosAvailable\"", json);
        Assert.DoesNotContain("\"PhotosAvailable\"", json);
    }
}
