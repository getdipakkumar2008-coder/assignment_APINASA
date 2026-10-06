using System.Reflection;
using MarsPhotos.Domain;

namespace MarsPhotos.Tests.Domain;

public class DomainModelTests
{
    [Fact]
    public void ErrorCodes_match_the_catalogue_exactly()
    {
        var actual = typeof(ErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            "DATE_UNRECOGNIZED", "DATE_INVALID", "DATE_OUT_OF_RANGE", "DATE_DUPLICATE", "API_NO_PHOTOS",
            "API_RATE_LIMITED", "API_AUTH", "API_UNAVAILABLE", "API_BAD_RESPONSE", "DL_FAILED",
            "DL_REJECTED_URL", "DL_TOO_LARGE", "IO_ERROR", "CONFIG_MISSING_KEY", "INPUT_FILE_ERROR",
            "RUN_IN_PROGRESS", "UNEXPECTED_ERROR",
        }.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DateResult_derives_counts_from_photo_outcomes()
    {
        var photos = new[]
        {
            new PhotoOutcome("a", PhotoStatus.Downloaded, "p/a.jpg", null, null, 10),
            new PhotoOutcome("b", PhotoStatus.Skipped, "p/b.jpg", null, null, 10),
            new PhotoOutcome("c", PhotoStatus.Skipped, "p/c.jpg", null, null, 10),
            new PhotoOutcome("d", PhotoStatus.Failed, null, ErrorCodes.DownloadFailed, "boom", null),
        };

        var result = new DateResult("x", new DateOnly(2017, 2, 27), DateResultStatus.PartialFailure, "RawImageFeed", null, 4, photos, []);

        Assert.Equal(1, result.Downloaded);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void RunTotals_sum_the_results()
    {
        var ok = new DateResult("a", new DateOnly(2017, 2, 27), DateResultStatus.Success, "RawImageFeed", null, 5,
            [new PhotoOutcome("1", PhotoStatus.Downloaded, "p", null, null, 1), new PhotoOutcome("2", PhotoStatus.Skipped, "p", null, null, 1)], []);
        var invalid = DateResult.WithoutLookup("April 31, 2018", null, DateResultStatus.InvalidDate, new ErrorInfo(ErrorCodes.DateInvalid, "Day 31 does not exist in April 2018"));
        var outside = DateResult.WithoutLookup("01/01/01", new DateOnly(2001, 1, 1), DateResultStatus.OutsideMissionRange);

        var totals = RunTotals.From([ok, invalid, outside]);

        Assert.Equal(new RunTotals(3, 2, 1, 1, 1, 1, 0), totals);
    }
}
