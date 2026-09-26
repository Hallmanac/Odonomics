using System.Net;

namespace Odonomics.Tests.TestSupport;

/// <summary>Answers every request with the same body and remembers each requested URL, so a test
/// can assert on the exact URL a source built rather than on what it did with the response.</summary>
public sealed class RecordingHttpMessageHandler(string body) : HttpMessageHandler
{
    public List<string> RequestedUrls { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUrls.Add(request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL"));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
