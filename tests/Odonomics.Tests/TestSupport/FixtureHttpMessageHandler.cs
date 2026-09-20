using System.Net;

namespace Odonomics.Tests.TestSupport;

/// <summary>Serves a fixed set of recorded responses keyed by request URL, so NHTSA contract
/// tests never touch the live network. Missing a mapping for a requested URL is a test bug, not
/// something to fall back on silently, so it throws rather than 404ing quietly.</summary>
public sealed class FixtureHttpMessageHandler(IReadOnlyDictionary<string, string> responsesByUrl) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL");
        if (!responsesByUrl.TryGetValue(url, out string? body))
        {
            throw new InvalidOperationException($"no fixture registered for {url}");
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };
        return Task.FromResult(response);
    }
}
