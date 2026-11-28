using System.Net;
using Xunit;

namespace MagicCollections.Tests;

internal sealed class RecordedHttpResponses(Action<HttpRequestMessage>? observeRequest = null) : HttpMessageHandler
{
    private readonly Dictionary<string, Func<Task<HttpResponseMessage>>> _responses = [];
    public List<string> Requests { get; } = [];

    public void RecordBody(string endpoint, string body) =>
        Record(endpoint, () => new(HttpStatusCode.OK) { Content = new StringContent(body) });

    public void Record(string endpoint, Func<HttpResponseMessage> respond) =>
        _responses[endpoint] = () => Task.FromResult(respond());

    public void RecordPending(string endpoint, Task<HttpResponseMessage> response) =>
        _responses[endpoint] = () => response;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        var endpoint = request.RequestUri!.AbsoluteUri;
        Requests.Add(endpoint);
        observeRequest?.Invoke(request);
        return _responses.TryGetValue(endpoint, out var respond)
            ? respond().WaitAsync(cancellationToken)
            : throw new InvalidOperationException($"No recording for {endpoint}");
    }
}
