using System.Net;
using Jellyfin.Plugin.Bazarr.Services;
using Jellyfin.Plugin.Bazarr.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Jellyfin.Plugin.Bazarr.Tests;

/// <summary>
/// Tests for BazarrService HTTP request construction.
/// These tests verify that requests to Bazarr are correctly formed.
/// Bugs here would mean:
/// - Authentication failures (missing/wrong API key header)
/// - 404 errors (wrong endpoints)
/// - Parse failures (wrong content type)
/// </summary>
public class BazarrServiceRequestTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<BazarrService>> _loggerMock;
    private readonly MockConfigProvider _configProvider;
    private readonly BazarrService _service;

    public BazarrServiceRequestTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _loggerMock = new Mock<ILogger<BazarrService>>();
        _configProvider = new MockConfigProvider();
        _service = new BazarrService(_httpClient, _cache, _loggerMock.Object, _configProvider);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// CRITICAL: Every request to Bazarr must include the X-API-KEY header.
    /// Without this, all requests would fail with 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task Requests_IncludeApiKeyHeader()
    {
        // Arrange
        var moviesJson = """{ "data": [] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act
        await _service.GetMoviesAsync();

        // Assert
        var request = _mockHandler.LastRequest;
        Assert.NotNull(request);
        Assert.True(request.Headers.Contains("X-API-KEY"));
    }

    /// <summary>
    /// Verify correct endpoint is called for movies.
    /// </summary>
    [Fact]
    public async Task GetMoviesAsync_CallsCorrectEndpoint()
    {
        // Arrange
        var moviesJson = """{ "data": [] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act
        await _service.GetMoviesAsync();

        // Assert
        var request = _mockHandler.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/api/movies", request.RequestUri?.AbsolutePath);
    }

    /// <summary>
    /// Verify correct endpoint is called for series.
    /// </summary>
    [Fact]
    public async Task GetSeriesAsync_CallsCorrectEndpoint()
    {
        // Arrange
        var seriesJson = """{ "data": [] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);

        // Act
        await _service.GetSeriesAsync();

        // Assert
        var request = _mockHandler.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/api/series", request.RequestUri?.AbsolutePath);
    }

    /// <summary>
    /// Verify episodes endpoint includes series ID parameter.
    /// </summary>
    [Fact]
    public async Task GetEpisodesAsync_IncludesSeriesIdInQuery()
    {
        // Arrange
        var episodesJson = """{ "data": [] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodesJson);

        // Act
        await _service.GetEpisodesAsync(42);

        // Assert
        var request = _mockHandler.LastRequest;
        Assert.NotNull(request);
        Assert.Contains("seriesid[]=42", request.RequestUri?.Query);
    }

    /// <summary>
    /// Verify languages endpoint is correct.
    /// </summary>
    [Fact]
    public async Task GetLanguagesAsync_CallsCorrectEndpoint()
    {
        // Arrange - Languages endpoint returns a direct array, not wrapped in { "data": [...] }
        var languagesJson = """[]""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, languagesJson);

        // Act
        await _service.GetLanguagesAsync();

        // Assert
        var request = _mockHandler.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/api/system/languages", request.RequestUri?.AbsolutePath);
    }

    /// <summary>
    /// Verify subtitle search for movies uses GET with correct endpoint and query params.
    /// </summary>
    [Fact]
    public async Task SearchMovieSubtitlesAsync_UsesGetWithCorrectEndpoint()
    {
        // Arrange - Bazarr API returns data wrapped in {"data": [...]}
        var subtitlesJson = """{"data": []}""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, subtitlesJson);

        // Act
        await _service.SearchMovieSubtitlesAsync(123, "en");

        // Assert
        var request = _mockHandler.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/api/providers/movies", request.RequestUri?.AbsolutePath);
        Assert.Contains("radarrid=123", request.RequestUri?.Query);
    }

    /// <summary>
    /// Verify subtitle search for episodes uses GET with correct endpoint.
    /// </summary>
    [Fact]
    public async Task SearchEpisodeSubtitlesAsync_UsesGetWithCorrectEndpoint()
    {
        // Arrange - Bazarr API returns data wrapped in {"data": [...]}
        var subtitlesJson = """{"data": []}""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, subtitlesJson);

        // Act - seriesId is required for episode search
        await _service.SearchEpisodeSubtitlesAsync(456, 123, "en");

        // Assert
        var request = _mockHandler.LastRequest;
        Assert.NotNull(request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith("/api/providers/episodes", request.RequestUri?.AbsolutePath);
        Assert.Contains("episodeid=456", request.RequestUri?.Query);
    }
}
