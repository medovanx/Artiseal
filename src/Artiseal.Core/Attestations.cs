using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Artiseal.Core;

/// <summary>One attestation GitHub holds for a digest: its Sigstore bundle as JSON text.</summary>
public sealed record AttestationBundle(long RepositoryId, string BundleJson);

/// <summary>Why a lookup returned nothing, so the window can say something exact.</summary>
public enum LookupOutcome
{
    Found,
    NoneForThisRepo,
    RepoNotFound,
    RateLimited,
    SignInRequired,
}

public sealed record LookupResult(LookupOutcome Outcome, IReadOnlyList<AttestationBundle> Bundles, string? Detail = null);

/// <summary>Reads GitHub's artifact attestations API for one repository.</summary>
public sealed class AttestationClient(HttpClient http, string? token = null)
{
    private const string Api = "https://api.github.com";

    public async Task<LookupResult> LookupAsync(RepoName repo, string sha256Hex, CancellationToken ct = default)
    {
        string url = $"{Api}/repos/{repo.Owner}/{repo.Name}/attestations/sha256:{sha256Hex}?per_page=30";
        using HttpRequestMessage request = NewRequest(url);
        using HttpResponseMessage response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // GitHub answers 404 both for "no such repo" and "no attestation for this digest".
            bool repoExists = await RepoExistsAsync(repo, ct);

            return new LookupResult(repoExists ? LookupOutcome.NoneForThisRepo : LookupOutcome.RepoNotFound, []);
        }
        if (IsRateLimited(response))
        {
            return new LookupResult(LookupOutcome.RateLimited, [], ResetTime(response));
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new LookupResult(LookupOutcome.SignInRequired, []);
        }
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        List<AttestationBundle> bundles = [];

        foreach (JsonElement item in doc.RootElement.GetProperty("attestations").EnumerateArray())
        {
            long repoId = item.TryGetProperty("repository_id", out JsonElement id) ? id.GetInt64() : 0;

            if (item.TryGetProperty("bundle", out JsonElement inline) && inline.ValueKind == JsonValueKind.Object)
            {
                bundles.Add(new AttestationBundle(repoId, inline.GetRawText()));
            }
            else if (item.TryGetProperty("bundle_url", out JsonElement link) && link.GetString() is { } bundleUrl)
            {
                bundles.Add(new AttestationBundle(repoId, await FetchBundleAsync(bundleUrl, ct)));
            }
        }

        return new LookupResult(bundles.Count > 0 ? LookupOutcome.Found : LookupOutcome.NoneForThisRepo, bundles);
    }

    /// <summary>The bundle link serves Snappy-compressed JSON; a plain JSON body is accepted too.</summary>
    private async Task<string> FetchBundleAsync(string url, CancellationToken ct)
    {
        // A signed storage link: sending GitHub's token there would leak it to another host.
        // One retry covers a passing DNS or connection blip; a second failure goes to the caller.
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return BundleText.Decode(await http.GetByteArrayAsync(url, ct));
            }
            catch (HttpRequestException) when (attempt == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    private async Task<bool> RepoExistsAsync(RepoName repo, CancellationToken ct)
    {
        using HttpRequestMessage request = NewRequest($"{Api}/repos/{repo.Owner}/{repo.Name}");
        using HttpResponseMessage response = await http.SendAsync(request, ct);

        return response.IsSuccessStatusCode;
    }

    private HttpRequestMessage NewRequest(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.UserAgent.ParseAdd("Artiseal/0.1");

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden
        && response.Headers.TryGetValues("x-ratelimit-remaining", out IEnumerable<string>? left)
        && left.FirstOrDefault() == "0";

    private static string? ResetTime(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-ratelimit-reset", out IEnumerable<string>? reset)
        && long.TryParse(reset.FirstOrDefault(), out long epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime().ToString("t")
            : null;
}

/// <summary>Turns a downloaded bundle body into JSON text.</summary>
public static class BundleText
{
    public static string Decode(byte[] body)
    {
        if (body.Length > 0 && body[0] == (byte)'{')
        {
            return Encoding.UTF8.GetString(body);
        }

        return Encoding.UTF8.GetString(Snappier.Snappy.DecompressToArray(body));
    }
}
