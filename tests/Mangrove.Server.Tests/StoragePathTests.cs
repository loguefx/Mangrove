using Mangrove.Server.Storage;
using Xunit;

namespace Mangrove.Server.Tests;

// Tests for the SMB/UNC path resolver (spec §5 + the "SMB path resolver" test requirement).
public class StoragePathTests
{
    [Fact]
    public void ParsesUncPath()
    {
        var p = StoragePath.ParseRemote(@"\\NAS\Manga\One Piece\v01.cbz");
        Assert.True(p.IsRemote);
        Assert.Equal("NAS", p.Host);
        Assert.Equal("Manga", p.Share);
        Assert.Equal(@"One Piece\v01.cbz", p.RelativePath);
    }

    [Fact]
    public void ParsesSmbUri()
    {
        var p = StoragePath.ParseRemote("smb://nas/manga/series/file.cbz");
        Assert.True(p.IsRemote);
        Assert.Equal("nas", p.Host);
        Assert.Equal("manga", p.Share);
        Assert.Equal(@"series\file.cbz", p.RelativePath);
    }

    [Fact]
    public void ParsesShareRootWithNoRelativePath()
    {
        var p = StoragePath.ParseRemote(@"\\nas\manga");
        Assert.Equal("nas", p.Host);
        Assert.Equal("manga", p.Share);
        Assert.Equal(string.Empty, p.RelativePath);
    }

    [Fact]
    public void ParsesForwardSlashUnc()
    {
        var p = StoragePath.ParseRemote("//server/share/dir/file.cbz");
        Assert.Equal("server", p.Host);
        Assert.Equal("share", p.Share);
        Assert.Equal(@"dir\file.cbz", p.RelativePath);
    }

    [Fact]
    public void AutoDetectsLocalPath()
    {
        var p = StoragePath.Parse(@"C:\Manga\Series");
        Assert.False(p.IsRemote);
        Assert.Equal(@"C:\Manga\Series", p.RelativePath);
    }

    [Fact]
    public void AutoDetectsRemotePath()
    {
        var p = StoragePath.Parse(@"\\nas\manga\x");
        Assert.True(p.IsRemote);
    }

    [Fact]
    public void CombineAppendsChildSegment()
    {
        var root = StoragePath.ParseRemote(@"\\nas\manga");
        var child = root.Combine("One Piece").Combine("v01.cbz");
        Assert.Equal(@"One Piece\v01.cbz", child.RelativePath);
        Assert.Equal(@"\\nas\manga\One Piece\v01.cbz", child.Canonical());
    }

    [Fact]
    public void CanonicalRoundTrips()
    {
        const string input = @"\\NAS\Manga\Series\file.cbz";
        Assert.Equal(input, StoragePath.ParseRemote(input).Canonical());
    }

    [Fact]
    public void NameReturnsFinalSegment()
    {
        Assert.Equal("file.cbz", StoragePath.ParseRemote(@"\\nas\manga\a\b\file.cbz").Name);
    }

    [Theory]
    [InlineData(@"\\nas")]              // missing share
    [InlineData("smb://host")]          // missing share
    [InlineData("C:/just/a/local/path")] // not a UNC/smb path
    public void RejectsInvalidRemotePaths(string bad)
    {
        Assert.Throws<FormatException>(() => StoragePath.ParseRemote(bad));
    }

    [Fact]
    public void EqualityIsCaseInsensitiveForRemote()
    {
        var a = StoragePath.ParseRemote(@"\\NAS\Manga\File.cbz");
        var b = StoragePath.ParseRemote(@"\\nas\manga\file.cbz");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
