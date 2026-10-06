using Xunit;

namespace Ref12.Feed.Tests;

public class SemVerTests
{
    [Fact]
    public void Orders_per_semver2()
    {
        // The canonical SemVer 2.0 precedence example, plus numeric-vs-lexical cases.
        var ordered = new[]
        {
            "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
            "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.2.0", "1.10.0", "2.0.0",
        };
        var shuffled = ordered.Reverse().ToArray();
        var sorted = shuffled.OrderBy(s => SemVer.Parse(s)).ToArray();
        Assert.Equal(ordered, sorted);
    }

    [Fact]
    public void Metadata_is_ignored_and_normalized()
    {
        Assert.Equal(0, SemVer.Parse("1.0.0+abc").CompareTo(SemVer.Parse("1.0.0+xyz")));
        Assert.Equal("1.0.0", SemVer.Parse("1.0+build").Normalized);
        Assert.Equal("1.0.0", SemVer.Parse("1.0.0.0").Normalized);
        Assert.Equal("1.0.0.1", SemVer.Parse("1.0.0.1").Normalized);
        Assert.Equal("1.0.0-beta.1", SemVer.Parse("1.0.0-beta.1+m").Normalized);
    }

    [Theory]
    [InlineData("")] [InlineData("a.b.c")] [InlineData("1.2.3.4.5")] [InlineData("1.0.0-")] [InlineData("1.0.0-a..b")] [InlineData("v1.0.0")]
    public void Rejects_invalid(string s) => Assert.False(SemVer.TryParse(s, out _));
}
