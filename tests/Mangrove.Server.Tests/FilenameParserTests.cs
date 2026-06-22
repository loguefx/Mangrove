using Mangrove.Server.Scanning;
using Xunit;

namespace Mangrove.Server.Tests;

// Tests for the scanner's filename parser (spec §8 + the "scanner" test requirement).
public class FilenameParserTests
{
    private readonly FilenameParser _parser = new();

    [Fact]
    public void ParsesVolumeAndChapter()
    {
        var r = _parser.Parse("Demo Series Vol.01 Ch.0001.cbz");
        Assert.Equal("Demo Series", r.Series);
        Assert.Equal(1f, r.Volume);
        Assert.Equal(1f, r.Chapter);
    }

    [Fact]
    public void ParsesCompactVolumeChapter()
    {
        var r = _parser.Parse("One Piece v01 c001.cbz");
        Assert.Equal("One Piece", r.Series);
        Assert.Equal(1f, r.Volume);
        Assert.Equal(1f, r.Chapter);
    }

    [Fact]
    public void ParsesChapterKeyword()
    {
        var r = _parser.Parse("Naruto Chapter 700.cbz");
        Assert.Equal("Naruto", r.Series);
        Assert.Equal(700f, r.Chapter);
        Assert.Null(r.Volume);
    }

    [Fact]
    public void ParsesTrailingNumberAsChapter()
    {
        var r = _parser.Parse("Bleach 001.cbz");
        Assert.Equal("Bleach", r.Series);
        Assert.Equal(1f, r.Chapter);
    }

    [Fact]
    public void ParsesDecimalChapter()
    {
        var r = _parser.Parse("Series Ch.10.5.cbz");
        Assert.Equal("Series", r.Series);
        Assert.Equal(10.5f, r.Chapter);
    }

    [Fact]
    public void HandlesSeriesWithNoTokens()
    {
        var r = _parser.Parse("Berserk.cbz");
        Assert.Equal("Berserk", r.Series);
        Assert.Null(r.Chapter);
        Assert.Null(r.Volume);
    }

    [Fact]
    public void DetectsSpecials()
    {
        var r = _parser.Parse("My Series Omake.cbz");
        Assert.True(r.IsSpecial);
    }

    [Fact]
    public void HandlesTarGzExtension()
    {
        var r = _parser.Parse("Series v02.tar.gz");
        Assert.Equal("Series", r.Series);
        Assert.Equal(2f, r.Volume);
    }
}
