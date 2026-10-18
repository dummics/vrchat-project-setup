using VrcSetup.Core;
using Xunit;

namespace VrcSetup.Tests;

public sealed class ProjectOrderingTests
{
    [Theory]
    [InlineData("name")]
    [InlineData("modified")]
    [InlineData("created")]
    public void ColumnCyclesForwardReverseAndUnsorted(string column)
    {
        var sort = ProjectOrdering.Cycle("default", column);
        Assert.Equal(column, sort);
        sort = ProjectOrdering.Cycle(sort, column); Assert.Equal(column + "-reverse", sort);
        Assert.Equal("default", ProjectOrdering.Cycle(sort, column));
        Assert.Equal(column, ProjectOrdering.Cycle("other", column));
    }
    [Theory]
    [InlineData("name", "a")]
    [InlineData("name-reverse", "b")]
    [InlineData("modified", "a")]
    [InlineData("modified-reverse", "b")]
    [InlineData("created", "b")]
    [InlineData("created-reverse", "a")]
    public void ColumnsUseTheirOwnDateAndDirection(string sort, string first)
    {
        var a = new ProjectSnapshot { Path = "a", Name = "A", LastUpdatedUtc = DateTime.UnixEpoch.AddDays(2), CreatedUtc = DateTime.UnixEpoch };
        var b = new ProjectSnapshot { Path = "b", Name = "B", LastUpdatedUtc = DateTime.UnixEpoch, CreatedUtc = DateTime.UnixEpoch.AddDays(1) };
        Assert.Equal(first, ProjectOrdering.Apply([b, a], sort)[0].Path);
    }
}
