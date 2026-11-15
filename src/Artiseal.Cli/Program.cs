using Artiseal.Core;
using Sigstore;

// Usage: artiseal <file> <owner/repo | github URL>
if (args.Length < 2 || !File.Exists(args[0]) || !RepoName.TryParse(args[1], out RepoName repo))
{
    Console.Error.WriteLine("Usage: artiseal <file> <owner/repo>");

    return 2;
}

using HttpClient http = new();
ProofChecker checker = new(new AttestationClient(http, Environment.GetEnvironmentVariable("GITHUB_TOKEN")), new SigstoreVerifier());
ProofReport report = await checker.CheckAsync(args[0], repo);

Console.WriteLine($"{report.Verdict}: {report.Message}");
Console.WriteLine($"  file    {report.FileName}");
Console.WriteLine($"  sha256  {report.Sha256}");

if (report.Facts is { } f)
{
    Console.WriteLine($"  repo     {f.Repository}");
    Console.WriteLine($"  workflow {f.Workflow}");
    Console.WriteLine($"  ref      {f.GitRef}");
    Console.WriteLine($"  commit   {f.Commit}");
    Console.WriteLine($"  trigger  {f.Trigger}");
    Console.WriteLine($"  runner   {f.Runner}");
    Console.WriteLine($"  run      {f.RunUrl}");
    Console.WriteLine($"  signed   {f.SignedAt:u}");
}

return report.Verdict == Verdict.Verified ? 0 : 1;
