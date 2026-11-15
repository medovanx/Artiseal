using Artiseal.Core;

namespace Artiseal.Tests;

public class RepoNameTests
{
    [Theory]
    [InlineData("cli/cli", "cli", "cli")]
    [InlineData("https://github.com/cli/cli", "cli", "cli")]
    [InlineData("github.com/cli/cli/releases/tag/v2.104.0", "cli", "cli")]
    [InlineData("https://github.com/microsoft/vscode.git", "microsoft", "vscode")]
    [InlineData("  owner-1/repo.name_x  ", "owner-1", "repo.name_x")]
    public void Parses(string text, string owner, string name)
    {
        Assert.True(RepoName.TryParse(text, out RepoName repo));
        Assert.Equal(new RepoName(owner, name), repo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("cli")]
    [InlineData("https://gitlab.com/cli/cli")]
    [InlineData("https://github.com.evil.example/cli/cli")]
    [InlineData("-bad/repo")]
    [InlineData("a/b/c")]
    public void Rejects(string text)
    {
        Assert.False(RepoName.TryParse(text, out _));
    }
}

public class BundleTextTests
{
    [Fact]
    public void Plain_json_passes_through()
    {
        Assert.Equal("{\"a\":1}", BundleText.Decode("{\"a\":1}"u8.ToArray()));
    }

    [Fact]
    public void Snappy_body_is_decompressed()
    {
        byte[] packed = Snappier.Snappy.CompressToArray("{\"mediaType\":\"x\"}"u8);

        Assert.Equal("{\"mediaType\":\"x\"}", BundleText.Decode(packed));
    }
}

public class DownloadSourceTests
{
    [Fact]
    public void Release_page_referrer_wins_over_cdn_host()
    {
        const string zone = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://github.com/cli/cli/releases/tag/v2.104.0\r\nHostUrl=https://release-assets.githubusercontent.com/github-production-release-asset/212613049/x\r\n";

        Assert.Equal(new RepoName("cli", "cli"), DownloadSource.GuessFromZoneText(zone));
    }

    [Fact]
    public void Direct_download_link_names_the_repo()
    {
        const string zone = "[ZoneTransfer]\nZoneId=3\nHostUrl=https://github.com/cli/cli/releases/download/v2.104.0/gh_2.104.0_windows_amd64.zip\n";

        Assert.Equal(new RepoName("cli", "cli"), DownloadSource.GuessFromZoneText(zone));
    }

    [Fact]
    public void Non_github_source_gives_no_guess()
    {
        Assert.Null(DownloadSource.GuessFromZoneText("[ZoneTransfer]\nZoneId=3\nHostUrl=https://example.com/tool.zip\n"));
    }
}
