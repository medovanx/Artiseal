using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Sigstore;

namespace Artiseal.Core;

/// <summary>
/// Finds which GitHub repos signed a file, from its hash alone, by searching Sigstore's public
/// transparency log (Rekor). The answer is only a lead: every repo it names still goes through
/// the full check, since the log entry itself is not verified here.
/// </summary>
public sealed class PublisherFinder(HttpClient http)
{
    private const string Rekor = "https://rekor.sigstore.dev/api/v1";

    // A file built in one release usually has one or two entries; a cap keeps a flooded hash cheap.
    private const int MaxEntries = 25;

    public async Task<IReadOnlyList<RepoName>> FindAsync(string sha256Hex, CancellationToken ct = default)
    {
        using HttpResponseMessage search = await http.PostAsJsonAsync($"{Rekor}/index/retrieve", new { hash = $"sha256:{sha256Hex}" }, ct);

        if (!search.IsSuccessStatusCode)
        {
            return [];
        }
        string[] uuids = (await search.Content.ReadFromJsonAsync<string[]>(ct) ?? []).Take(MaxEntries).ToArray();

        if (uuids.Length == 0)
        {
            return [];
        }

        using HttpResponseMessage fetch = await http.PostAsJsonAsync($"{Rekor}/log/entries/retrieve", new { entryUUIDs = uuids }, ct);

        if (!fetch.IsSuccessStatusCode)
        {
            return [];
        }
        using JsonDocument doc = JsonDocument.Parse(await fetch.Content.ReadAsStreamAsync(ct));
        List<RepoName> repos = [];

        foreach (JsonElement batch in doc.RootElement.EnumerateArray())
        {
            foreach (JsonProperty entry in batch.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("body", out JsonElement body) && body.GetString() is { } b64)
                {
                    foreach (RepoName repo in ReposInEntryBody(b64))
                    {
                        if (!repos.Contains(repo))
                        {
                            repos.Add(repo);
                        }
                    }
                }
            }
        }

        return repos;
    }

    /// <summary>The source repos named by the GitHub Actions certificates inside one Rekor entry body.</summary>
    public static IEnumerable<RepoName> ReposInEntryBody(string base64Body)
    {
        JsonElement body;

        try
        {
            body = JsonDocument.Parse(Convert.FromBase64String(base64Body)).RootElement;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            yield break;
        }

        foreach (string pem in CertificatesIn(body))
        {
            RepoName? repo = RepoFromCertificate(pem);

            if (repo is not null)
            {
                yield return repo;
            }
        }
    }

    /// <summary>Signing certificates sit in different places per entry kind (dsse, intoto, hashedrekord).</summary>
    private static IEnumerable<string> CertificatesIn(JsonElement body)
    {
        List<string> found = [];
        Collect(body, found);

        return found;

        static void Collect(JsonElement node, List<string> found)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty p in node.EnumerateObject())
                    {
                        if (p.Name is "verifier" or "publicKey" or "content" && p.Value.ValueKind == JsonValueKind.String && Pem(p.Value.GetString()) is { } pem)
                        {
                            found.Add(pem);
                        }
                        else
                        {
                            Collect(p.Value, found);
                        }
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in node.EnumerateArray())
                    {
                        Collect(item, found);
                    }
                    break;
            }
        }

        static string? Pem(string? base64)
        {
            try
            {
                string text = Encoding.ASCII.GetString(Convert.FromBase64String(base64 ?? string.Empty));

                return text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal) ? text : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    private const string ActionsIssuer = "https://token.actions.githubusercontent.com";

    private static RepoName? RepoFromCertificate(string pem)
    {
        try
        {
            using X509Certificate2 cert = X509Certificate2.CreateFromPem(pem);
            FulcioCertificateExtensions x = FulcioCertificateExtensions.FromCertificate(cert);

            // Only GitHub Actions identities name a repo the rest of the check can verify.
            if (x.Issuer != ActionsIssuer)
            {
                return null;
            }

            return RepoName.TryParse(x.SourceRepositoryUri, out RepoName repo) ? repo : null;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return null;
        }
    }
}
