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
