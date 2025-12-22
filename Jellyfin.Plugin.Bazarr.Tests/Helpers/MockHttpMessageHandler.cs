using System.Net;

namespace Jellyfin.Plugin.Bazarr.Tests.Helpers;

/// <summary>
/// Mock HTTP message handler for testing HTTP client calls.
/// Captures the request and returns a configurable response.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();
    private readonly Dictionary<string, (HttpStatusCode statusCode, string content, string contentType)> _endpointResponses = new();
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
    /// Adds a response for a specific endpoint path.
    /// </summary>
    public void AddResponse(string path, HttpStatusCode statusCode, string content, string contentType = "application/json")
    {
        _endpointResponses[path] = (statusCode, content, contentType);
    }

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

        // Check if we have a response for this specific endpoint
        var path = request.RequestUri?.PathAndQuery ?? string.Empty;
        if (_endpointResponses.TryGetValue(path, out var endpointResponse))
        {
            var response = new HttpResponseMessage(endpointResponse.statusCode)
            {
                RequestMessage = request
            };

            // Parse content type to extract just the media type
            var contentTypeValue = endpointResponse.contentType;
            var semicolonIndex = contentTypeValue.IndexOf(';', StringComparison.Ordinal);
            var mediaType = semicolonIndex >= 0 ? contentTypeValue.Substring(0, semicolonIndex).Trim() : contentTypeValue;

            response.Content = new StringContent(endpointResponse.content, System.Text.Encoding.UTF8, mediaType);

            // Set the full content type header if it includes parameters like charset
            if (semicolonIndex >= 0)
            {
                response.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentTypeValue);
            }

            return Task.FromResult(response);
        }

        // Fall back to queued responses
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"No response configured for request: {request.Method} {request.RequestUri}");
        }

        var queuedResponse = _responses.Dequeue();

        if (queuedResponse is ExceptionResponse exResponse)
        {
            throw exResponse.Exception;
        }

        return Task.FromResult(queuedResponse);
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

