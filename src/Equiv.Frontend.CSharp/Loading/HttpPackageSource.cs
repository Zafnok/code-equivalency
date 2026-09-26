using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace Equiv.Frontend.CSharp.Loading;

/// <summary>The only code that touches the network: one HTTP GET behind <see cref="PackageFeed"/>'s seam (M3-029).</summary>
[ExcludeFromCodeCoverage(Justification = "M3-029: the network factory behind the package feed seam; every caller is tested through a fake feed, and the parity job runs it against nuget.org")]
internal static class HttpPackageSource
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>The resource's bytes, or null when the source does not have it.</summary>
    /// <exception cref="HttpRequestException">Any other failure.</exception>
    public static async Task<byte[]?> GetAsync(Uri uri, CancellationToken ct)
    {
        using HttpResponseMessage response = await Client.GetAsync(uri, ct).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.EnsureSuccessStatusCode().Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }
}
