using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sigstore;

namespace Artiseal.Core;

public enum Verdict
{
    /// <summary>A bundle verified: signed by a GitHub Actions run in the named repo, for these exact bytes.</summary>
    Verified,
    /// <summary>The repo exists but holds no attestation for this file's hash.</summary>
    NoProof,
    /// <summary>Attestations exist for the hash, but none verified for this repo.</summary>
    Failed,
    /// <summary>Could not get an answer (repo missing, rate limit, network).</summary>
    Unknown,
}

/// <summary>Where and how the file was built, read from the verified signing certificate.</summary>
public sealed record BuildFacts(
    string Repository,
    string? Workflow,
    string? GitRef,
    string? Commit,
    string? Trigger,
    string? RunUrl,
    string? Runner,
    DateTimeOffset? SignedAt);

public sealed record ProofReport(
    Verdict Verdict,
    string FileName,
    string Sha256,
    RepoName Repo,
    BuildFacts? Facts,
    string Message);

/// <summary>Hashes a file, fetches GitHub's attestations for it and verifies them with Sigstore.</summary>
public sealed class ProofChecker(AttestationClient client, SigstoreVerifier verifier)
{
    public async Task<ProofReport> CheckAsync(string path, RepoName repo, CancellationToken ct = default)
    {
        string fileName = Path.GetFileName(path);
        string hex = await HashAsync(path, ct);
        LookupResult lookup = await client.LookupAsync(repo, hex, ct);

        switch (lookup.Outcome)
        {
            case LookupOutcome.RepoNotFound:
                return new ProofReport(Verdict.Unknown, fileName, hex, repo, null, $"{repo} doesn't exist on GitHub, or it's private.");
            case LookupOutcome.RateLimited:
                return new ProofReport(Verdict.Unknown, fileName, hex, repo, null,
                    lookup.Detail is null ? "GitHub's hourly limit is used up. Try again later." : $"GitHub's hourly limit is used up. Try again after {lookup.Detail}.");
            case LookupOutcome.SignInRequired:
                return new ProofReport(Verdict.Unknown, fileName, hex, repo, null, "GitHub wants a sign-in to answer for this repo.");
            case LookupOutcome.NoneForThisRepo:
                return new ProofReport(Verdict.NoProof, fileName, hex, repo, null, $"{repo} has published no build proof for this exact file.");
        }

        string? lastFailure = null;

        foreach (AttestationBundle item in lookup.Bundles)
        {
            SigstoreBundle bundle;

            try
            {
                bundle = SigstoreBundle.Deserialize(item.BundleJson);
            }
            catch (Exception ex)
            {
                lastFailure = $"A proof could not be read: {ex.Message}";
                continue;
            }

            (bool ok, VerificationResult? result) = await VerifyAsync(Convert.FromHexString(hex), bundle, repo, ct);

            if (ok && result?.SignerIdentity is { } signer)
            {
                return new ProofReport(Verdict.Verified, fileName, hex, repo, ToFacts(repo, signer, result),
                    $"Built by GitHub Actions in {repo}.");
            }
            lastFailure = result?.FailureReason ?? "The proof did not verify.";
        }

        return new ProofReport(Verdict.Failed, fileName, hex, repo, null,
            $"GitHub has proofs for this file, but none of them check out for {repo}. {lastFailure}");
    }

    /// <summary>
    /// The cryptographic check: the bundle's signature, certificate chain and transparency log entry are valid,
    /// its signed subject is this digest, and the signer is a GitHub Actions run in this repo.
    /// </summary>
    public async Task<(bool Success, VerificationResult? Result)> VerifyAsync(
        byte[] sha256, SigstoreBundle bundle, RepoName repo, CancellationToken ct = default)
    {
        // The library's own GitHub helper matches names case-sensitively and unescaped ("cl." would match "cli"),
        // so the identity is anchored and escaped here, and the source repo is compared below.
        VerificationPolicy policy = new()
        {
            CertificateIdentity = new CertificateIdentity
            {
                Issuer = ActionsIssuer,
                SubjectAlternativeNamePattern = $"(?i)^https://github\\.com/{Regex.Escape(repo.Owner)}/{Regex.Escape(repo.Name)}/",
            },
        };

        (bool ok, VerificationResult? result) = await verifier.TryVerifyDigestAsync(sha256, HashAlgorithmType.Sha256, bundle, policy, ct);

        if (!ok || result is null)
        {
            return (false, result);
        }

        // Sigstore 1.1.1 checks a DSSE envelope's signature but never compares its subjects with the artifact,
        // so the file-to-proof binding is enforced here, on the statement the signature covered.
        string? problem = RepoProblem(result.SignerIdentity, repo) ?? SubjectProblem(result.Statement, Convert.ToHexStringLower(sha256));

        return problem is null ? (true, result) : (false, result with { FailureReason = problem });
    }

    private const string ActionsIssuer = "https://token.actions.githubusercontent.com";

    /// <summary>Null when the verified certificate says the build ran in exactly this repo (GitHub names ignore case).</summary>
    public static string? RepoProblem(VerifiedIdentity? signer, RepoName repo)
    {
        string? source = signer?.Extensions?.SourceRepositoryUri;

        return string.Equals(source, $"https://github.com/{repo.Owner}/{repo.Name}", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"The proof was built in {source ?? "an unnamed repo"}, not {repo}.";
    }

    private const string SlsaProvenance = "https://slsa.dev/provenance/";

    /// <summary>Null when the signed statement is build provenance naming this exact sha256.</summary>
    public static string? SubjectProblem(InTotoStatement? statement, string sha256Hex)
    {
        if (statement is null)
        {
            return "The proof carries no signed statement about a file.";
        }
        if (statement.PredicateType is not { } type || !type.StartsWith(SlsaProvenance, StringComparison.Ordinal))
        {
            return "The proof is not a build record.";
        }
        bool named = statement.Subject.Any(s =>
            s.Digest.TryGetValue("sha256", out string? digest) && string.Equals(digest, sha256Hex, StringComparison.OrdinalIgnoreCase));

        return named ? null : "The proof is real, but it is for a different file.";
    }

    private static BuildFacts ToFacts(RepoName repo, VerifiedIdentity signer, VerificationResult result)
    {
        FulcioCertificateExtensions? x = signer.Extensions;
        DateTimeOffset? signedAt = result.VerifiedTimestamps.Count > 0
            ? result.VerifiedTimestamps.Min(t => t.Timestamp)
            : null;

        return new BuildFacts(
            Repository: x?.SourceRepositoryUri ?? $"https://github.com/{repo}",
            Workflow: WorkflowPath(x?.BuildConfigUri ?? signer.SubjectAlternativeName),
            GitRef: x?.SourceRepositoryRef,
            Commit: x?.SourceRepositoryDigest,
            Trigger: x?.BuildTrigger,
            RunUrl: x?.RunInvocationUri,
            Runner: x?.RunnerEnvironment,
            SignedAt: signedAt);
    }

    /// <summary>"https://github.com/o/r/.github/workflows/release.yml@refs/tags/v1" becomes ".github/workflows/release.yml".</summary>
    private static string? WorkflowPath(string? uri)
    {
        if (uri is null)
        {
            return null;
        }
        int start = uri.IndexOf("/.github/", StringComparison.Ordinal);
        int at = uri.IndexOf('@', start < 0 ? 0 : start);

        return start < 0 ? uri : uri[(start + 1)..(at < 0 ? uri.Length : at)];
    }

    public static async Task<string> HashAsync(string path, CancellationToken ct = default)
    {
        await using FileStream file = File.OpenRead(path);
        byte[] digest = await SHA256.HashDataAsync(file, ct);

        return Convert.ToHexStringLower(digest);
    }
}
