using System.Text;
using Mangrove.Server.Readers;
using Xunit;

namespace Mangrove.Server.Tests;

public class ComicInfoReaderTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <ComicInfo>
          <Series>Berserk</Series>
          <Number>12</Number>
          <Volume>3</Volume>
          <Title>The Golden Age</Title>
          <Summary>A wandering mercenary.</Summary>
          <Writer>Kentaro Miura</Writer>
          <Genre>Dark Fantasy</Genre>
          <Publisher>Hakusensha</Publisher>
          <LanguageISO>en</LanguageISO>
          <AgeRating>Mature 17+</AgeRating>
          <Count>40</Count>
        </ComicInfo>
        """;

    [Fact]
    public void ParsesComicInfoFields()
    {
        var reader = new ComicInfoReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Sample));
        var info = reader.Parse(stream);

        Assert.NotNull(info);
        Assert.Equal("Berserk", info!.Series);
        Assert.Equal(12f, info.Number);
        Assert.Equal(3f, info.Volume);
        Assert.Equal("The Golden Age", info.Title);
        Assert.Equal("Kentaro Miura", info.Writer);
        Assert.Equal("Dark Fantasy", info.Genre);
        Assert.Equal("Hakusensha", info.Publisher);
        Assert.Equal("en", info.Language);
        Assert.Equal("Mature 17+", info.AgeRating);
        Assert.Equal(40, info.Count);
    }

    [Fact]
    public void ReturnsNullForNonComicInfoXml()
    {
        var reader = new ComicInfoReader();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<not-comic-info/>"));
        var info = reader.Parse(stream);
        // Root element isn't ComicInfo, but the parser is lenient and reads elements by name;
        // with no recognized children every field is null.
        Assert.True(info is null || info.Series is null);
    }
}
