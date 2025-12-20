using System.Net;

namespace Jellyfin.Plugin.Bazarr.Tests.Helpers;

/// <summary>
/// Mock HTTP message handler for testing HTTP client calls.
/// Captures the request and returns a configurable response.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly List<HttpRequestMessage> _requests = new();

    /// <summary>
    /// Gets all captured requests.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> CapturedRequests => _requests;

    /// <summary>
    /// Gets the last captured request.
    /// </summary>
    public HttpRequestMessage? LastRequest => _requests.Count > 0 ? _requests[^1] : null;

    /// <summary>
    /// Queues a response to be returned on the next HTTP call.
    /// </summary>
    public void QueueResponse(HttpStatusCode statusCode, string content)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };
        _responses.Enqueue(response);
    }

    /// <summary>
    /// Queues a response to be returned on the next HTTP call.
    /// </summary>
    public void QueueResponse(HttpResponseMessage response)
    {
        _responses.Enqueue(response);
    }

    /// <summary>
    /// Queues an exception to be thrown on the next HTTP call.
    /// </summary>
    public void QueueException(Exception exception)
    {
        _responses.Enqueue(new ExceptionResponse(exception));
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _requests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"No response queued for request: {request.Method} {request.RequestUri}");
        }

        var response = _responses.Dequeue();

        if (response is ExceptionResponse exResponse)
        {
            throw exResponse.Exception;
        }

        return Task.FromResult(response);
    }

    private class ExceptionResponse : HttpResponseMessage
    {
        public Exception Exception { get; }

        public ExceptionResponse(Exception exception)
        {
            Exception = exception;
        }
    }
}
