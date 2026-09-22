using System.Net;

namespace Odonomics.Tests.TestSupport;

/// <summary>Returns the same status code and empty body for every request, for source-failure
/// tests that need an HTTP error rather than a recorded fixture.</summary>
public sealed class StatusCodeHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent("") });
}
