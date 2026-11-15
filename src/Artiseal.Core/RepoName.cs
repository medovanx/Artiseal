using System.Text.RegularExpressions;

namespace Artiseal.Core;

/// <summary>An owner/name pair, accepted as "owner/name" or any github.com URL inside the repo.</summary>
public sealed partial record RepoName(string Owner, string Name)
{
    public override string ToString() => $"{Owner}/{Name}";

    public static bool TryParse(string? text, out RepoName repo)
    {
        repo = new RepoName(string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        Match url = UrlForm().Match(text.Trim());
        Match plain = PlainForm().Match(text.Trim());
        Match hit = url.Success ? url : plain;

        if (!hit.Success)
        {
            return false;
        }
        string name = hit.Groups["name"].Value;

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }
        repo = new RepoName(hit.Groups["owner"].Value, name);

        return true;
    }

    // GitHub owners: letters, digits, single hyphens; repo names add dots and underscores.
    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?github\.com/(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<name>[A-Za-z0-9._-]{1,100})(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlForm();

    [GeneratedRegex(@"^(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]{0,38}))/(?<name>[A-Za-z0-9._-]{1,100})$")]
    private static partial Regex PlainForm();
}
