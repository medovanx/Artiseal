using System.Text.Json;

namespace Artiseal.Core;

/// <summary>The repos checked lately, newest first, kept in the user's local app data.</summary>
public sealed class RecentRepos(string filePath)
{
    private const int Keep = 12;

    public static RecentRepos Default() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Artiseal", "recent.json"));

    public IReadOnlyList<string> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(filePath)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Remember(RepoName repo)
    {
        string text = repo.ToString();
        List<string> list = [text, .. Load().Where(r => !string.Equals(r, text, StringComparison.OrdinalIgnoreCase))];

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, JsonSerializer.Serialize(list.Take(Keep).ToList()));
    }
}
