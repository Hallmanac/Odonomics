using System.Net;

namespace Odonomics.Tests.TestSupport;

/// <summary>Serves one queued (status, body) response per request to a given URL, in order, so a
/// test can prove a retry-then-succeed or retry-then-still-fail sequence against NhtsaClient's
/// retry-once behavior. Each URL's queue must hold exactly as many responses as the test expects
/// calls to that URL: an empty queue on a request is a test bug (an unexpected extra attempt), so
/// it throws rather than repeating the last response silently.</summary>
public sealed class SequencedHttpMessageHandler(IReadOnlyDictionary<string, Queue<(HttpStatusCode Status, string Body)>> responsesByUrl) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL");
        if (!responsesByUrl.TryGetValue(url, out Queue<(HttpStatusCode Status, string Body)>? queue) || queue.Count == 0)
        {
            throw new InvalidOperationException($"no queued fixture response left for {url}");
        }

        (HttpStatusCode status, string body) = queue.Dequeue();
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        return Task.FromResult(response);
    }
}
