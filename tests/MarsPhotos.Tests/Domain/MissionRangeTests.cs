using MarsPhotos.Domain;

namespace MarsPhotos.Tests.Domain;

public class MissionRangeTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Theory]
    [InlineData("curiosity", "2012-08-06", true)]  // landing day, inclusive
    [InlineData("curiosity", "2012-08-05", false)] // day before landing
    [InlineData("curiosity", "2026-10-06", true)]  // today, inclusive
    [InlineData("curiosity", "2026-10-07", false)] // future
    [InlineData("curiosity", "2017-02-27", true)]
    [InlineData("opportunity", "2004-01-25", true)]
    [InlineData("opportunity", "2018-06-10", true)]
    [InlineData("opportunity", "2018-06-11", false)]
    [InlineData("opportunity", "2004-01-24", false)]
    [InlineData("spirit", "2004-01-04", true)]
    [InlineData("spirit", "2010-03-22", true)]
    [InlineData("spirit", "2010-03-23", false)]
    [InlineData("CURIOSITY", "2017-02-27", true)]  // case-insensitive
    [InlineData("perseverance", "2022-01-01", false)] // unknown rover
    [Trait("AC", "AC-2")]
    public void Contains_respects_inclusive_mission_boundaries(string rover, string date, bool expected)
    {
        Assert.Equal(expected, MissionRange.Contains(rover, DateOnly.Parse(date), Today));
    }

    [Theory]
    [InlineData("curiosity")]
    [InlineData("opportunity")]
    [InlineData("spirit")]
    public void IsKnownRover_accepts_supported_rovers(string rover) => Assert.True(MissionRange.IsKnownRover(rover));

    [Fact]
    public void IsKnownRover_rejects_unknown() => Assert.False(MissionRange.IsKnownRover("sojourner"));
}
