using Artiseal.Core;
using Sigstore;

// Usage: artiseal <file> [owner/repo | github URL]
// Without a repo, the publishers are looked up in Sigstore's public log by the file's hash.
if (args.Length < 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: artiseal <file> [owner/repo]");

    return 2;
}

Task warm = Trust.WarmAsync(); // the slow download runs while the file is hashed and looked up
using HttpClient http = new();
ProofChecker checker = new(new AttestationClient(http, Environment.GetEnvironmentVariable("GITHUB_TOKEN")), Trust.NewVerifier());
List<RepoName> repos = [];

if (args.Length > 1)
{
    if (!RepoName.TryParse(args[1], out RepoName given))
    {
        Console.Error.WriteLine($"Not a GitHub repo: {args[1]}");

        return 2;
    }
    repos.Add(given);
}
else
{
    string hash = await ProofChecker.HashAsync(args[0]);
    repos.AddRange(await new PublisherFinder(http).FindAsync(hash));

    if (repos.Count == 0)
    {
        Console.WriteLine("NoProof: no public build record names this file. Pass owner/repo to ask GitHub directly.");

        return 1;
    }
    Console.WriteLine($"Signed by: {string.Join(", ", repos)}");
}

int exit = 1;

foreach (RepoName repo in repos)
{
    ProofReport report;

    try
    {
        report = await checker.CheckAsync(args[0], repo);
    }
    catch (HttpRequestException ex)
    {
        Console.WriteLine($"Unknown: couldn't reach GitHub to check {repo} ({ex.Message})");
        continue;
    }

    Console.WriteLine($"{report.Verdict}: {report.Message}");

    if (report.Facts is { } f)
    {
        Console.WriteLine($"  repo     {f.Repository}");
        Console.WriteLine($"  workflow {f.Workflow}");
        Console.WriteLine($"  ref      {f.GitRef}");
        Console.WriteLine($"  commit   {f.Commit}");
        Console.WriteLine($"  run      {f.RunUrl}");
        Console.WriteLine($"  signed   {f.SignedAt:u}");
        exit = 0;
    }
}

return exit;
