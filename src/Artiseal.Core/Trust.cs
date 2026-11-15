using Sigstore;

namespace Artiseal.Core;

/// <summary>
/// One Sigstore trust root for the whole process. Fetching it is the slowest step, so the app
/// starts it as soon as it opens and every check after that reuses it.
/// </summary>
public static class Trust
{
    private static readonly TufTrustRootProvider Provider = new(TufTrustRootProvider.ProductionUrl);

    public static SigstoreVerifier NewVerifier() => new(Provider);

    /// <summary>Starts or joins the trust root download; failures surface later, in the check itself.</summary>
    public static async Task WarmAsync()
    {
        try
        {
            await Provider.GetTrustRootAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }
    }
}
