using Artiseal.Core;
using Sigstore;

namespace Artiseal.Tests;

/// <summary>
/// Runs the real Sigstore check on GitHub's real proof for gh_2.104.0_windows_amd64.zip.
/// Needs the network once, for Sigstore's trust root.
/// </summary>
public class VerificationTests
{
    private const string RealDigest = "08c06d10482f12c478018cd259f64acbd5f625712cadf331aad19ba973c879e5";

    private static readonly RepoName CliRepo = new("cli", "cli");

    // GitHub returns two proofs for this file: the Actions build provenance and GitHub's own release proof.
    private static SigstoreBundle RealBundle() => Fixture("gh_2.104.0_windows_amd64.provenance.sn");

    private static SigstoreBundle Fixture(string name)
    {
        byte[] body = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

        return SigstoreBundle.Deserialize(BundleText.Decode(body));
    }

    [Fact]
    public async Task GitHub_release_proof_is_not_taken_as_build_provenance()
    {
        // Signed by GitHub's own instance, not an Actions run in the public log, so the build check must not pass it.
        (bool ok, _) = await Checker().VerifyAsync(
            Convert.FromHexString(RealDigest), Fixture("gh_2.104.0_windows_amd64.release.sn"), CliRepo, TestContext.Current.CancellationToken);

        Assert.False(ok);
    }

    private static ProofChecker Checker() => new(new AttestationClient(new HttpClient()), new SigstoreVerifier());

    [Fact]
    public async Task Real_file_and_right_repo_verifies()
    {
        (bool ok, VerificationResult? result) =
            await Checker().VerifyAsync(Convert.FromHexString(RealDigest), RealBundle(), CliRepo, TestContext.Current.CancellationToken);

        Assert.True(ok, result?.FailureReason);
        Assert.Equal("https://github.com/cli/cli", result!.SignerIdentity!.Extensions!.SourceRepositoryUri);
    }

    [Fact]
    public async Task One_changed_bit_in_the_hash_fails()
    {
        byte[] digest = Convert.FromHexString(RealDigest);
        digest[0] ^= 0x01;

        (bool ok, VerificationResult? result) = await Checker().VerifyAsync(digest, RealBundle(), CliRepo, TestContext.Current.CancellationToken);

        Assert.False(ok);
        Assert.Equal("The proof is real, but it is for a different file.", result?.FailureReason);
    }

    [Fact]
    public async Task Another_real_asset_of_the_same_release_still_matches_its_own_subject()
    {
        // The provenance lists every asset it built; a sibling asset's hash must match its own entry, not ours.
        SigstoreBundle bundle = RealBundle();
        InTotoSubject other = bundle.DsseEnvelope!.GetStatement()!.Subject.First(s => s.Digest["sha256"] != RealDigest);

        (bool ok, _) = await Checker().VerifyAsync(Convert.FromHexString(other.Digest["sha256"]), bundle, CliRepo, TestContext.Current.CancellationToken);

        Assert.True(ok);
    }

    [Fact]
    public async Task Real_proof_checked_as_another_repo_fails()
    {
        // The cli/cli proof must not pass for a look-alike repo, or anyone could borrow a trusted name.
        (bool ok, _) = await Checker().VerifyAsync(
            Convert.FromHexString(RealDigest), RealBundle(), new RepoName("cli", "cli-fork"), TestContext.Current.CancellationToken);

        Assert.False(ok);
    }

    [Fact]
    public async Task Real_proof_checked_as_another_owner_fails()
    {
        (bool ok, _) = await Checker().VerifyAsync(
            Convert.FromHexString(RealDigest), RealBundle(), new RepoName("evil", "cli"), TestContext.Current.CancellationToken);

        Assert.False(ok);
    }
}
