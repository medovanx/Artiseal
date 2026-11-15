using System.Text.Json;
using Artiseal.Core;

namespace Artiseal.Tests;

public class PublisherFinderTests
{
    private const string RealDigest = "08c06d10482f12c478018cd259f64acbd5f625712cadf331aad19ba973c879e5";

    [Fact]
    public void Reads_the_repo_from_a_saved_log_entry()
    {
        // The Rekor entry for gh_2.104.0_windows_amd64.zip, as the log served it.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rekor-entry-cli.json")));
        string body = doc.RootElement.EnumerateObject().First().Value.GetProperty("body").GetString()!;

        Assert.Equal([new RepoName("cli", "cli")], PublisherFinder.ReposInEntryBody(body));
    }

    [Fact]
    public void Garbage_body_names_nobody()
    {
        Assert.Empty(PublisherFinder.ReposInEntryBody("not base64 !!"));
        Assert.Empty(PublisherFinder.ReposInEntryBody(Convert.ToBase64String("{\"kind\":\"dsse\"}"u8)));
    }

    [Fact]
    public async Task Live_log_finds_the_publisher_of_a_real_file()
    {
        IReadOnlyList<RepoName> repos = await new PublisherFinder(new HttpClient()).FindAsync(RealDigest, TestContext.Current.CancellationToken);

        Assert.Contains(new RepoName("cli", "cli"), repos);
    }

    [Fact]
    public async Task Live_log_finds_nobody_for_an_unknown_file()
    {
        IReadOnlyList<RepoName> repos = await new PublisherFinder(new HttpClient()).FindAsync(new string('0', 64), TestContext.Current.CancellationToken);

        Assert.Empty(repos);
    }
}
