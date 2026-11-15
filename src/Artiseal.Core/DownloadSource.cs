namespace Artiseal.Core;

/// <summary>
/// Guesses the publishing repo from the download address Windows stores beside a file
/// (the Zone.Identifier stream). Browsers write it; some download managers don't.
/// </summary>
public static class DownloadSource
{
    public static RepoName? GuessRepo(string path)
    {
        string? zone = ReadZoneIdentifier(path);

        return zone is null ? null : GuessFromZoneText(zone);
    }

    /// <summary>Reads ReferrerUrl first (the release page), then HostUrl (the download link, often a CDN).</summary>
    public static RepoName? GuessFromZoneText(string zoneText)
    {
        string? referrer = null;
        string? host = null;

        foreach (string raw in zoneText.Split('\n'))
        {
            string line = raw.Trim();

            if (line.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase))
            {
                referrer = line["ReferrerUrl=".Length..];
            }
            else if (line.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase))
            {
                host = line["HostUrl=".Length..];
            }
        }

        foreach (string? url in new[] { referrer, host })
        {
            if (RepoName.TryParse(url, out RepoName repo))
            {
                return repo;
            }
        }

        return null;
    }

    private static string? ReadZoneIdentifier(string path)
    {
        try
        {
            // NTFS alternate data stream; absent on FAT drives and on files that never came from the internet.
            return File.ReadAllText(path + ":Zone.Identifier");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
