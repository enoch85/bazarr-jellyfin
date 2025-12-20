using System.Net;
using Jellyfin.Plugin.Bazarr.Api.Models;
using Jellyfin.Plugin.Bazarr.Services;
using Jellyfin.Plugin.Bazarr.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Jellyfin.Plugin.Bazarr.Tests;

/// <summary>
/// Tests for BazarrService caching behavior.
/// Caching is critical because:
/// - Without caching, every subtitle search would fetch ALL movies/series from Bazarr
/// - This would hammer the Bazarr API and cause slow responses
/// - A bug here means performance degradation or rate limiting
/// </summary>
public class BazarrServiceCachingTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<BazarrService>> _loggerMock;
    private readonly MockConfigProvider _configProvider;
    private readonly BazarrService _service;

    public BazarrServiceCachingTests()
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
    /// CRITICAL: Second call to GetMoviesAsync must use cache, not make HTTP request.
    /// If this fails, we'd make excessive API calls to Bazarr.
    /// </summary>
    [Fact]
    public async Task GetMoviesAsync_SecondCall_UsesCacheNotHttp()
    {
        // Arrange - Only one response queued
        var moviesJson = """{ "data": [{ "radarrId": 1, "title": "Test", "tmdbId": 123 }] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, moviesJson);

        // Act - Call twice
        var result1 = await _service.GetMoviesAsync();
        var result2 = await _service.GetMoviesAsync();

        // Assert - Only ONE HTTP request should have been made
        Assert.Single(_mockHandler.CapturedRequests);
        Assert.Single(result1);
        Assert.Single(result2);
        // Data should be same
        Assert.Equal(result1[0].RadarrId, result2[0].RadarrId);
    }

    /// <summary>
    /// CRITICAL: Same test for series.
    /// </summary>
    [Fact]
    public async Task GetSeriesAsync_SecondCall_UsesCacheNotHttp()
    {
        // Arrange
        var seriesJson = """{ "data": [{ "sonarrSeriesId": 1, "title": "Test", "tvdbId": 123 }] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, seriesJson);

        // Act
        var result1 = await _service.GetSeriesAsync();
        var result2 = await _service.GetSeriesAsync();

        // Assert
        Assert.Single(_mockHandler.CapturedRequests);
        Assert.Single(result1);
        Assert.Single(result2);
    }

    /// <summary>
    /// Episodes are cached per-series.
    /// Same series ID should use cache, different series ID should make new request.
    /// </summary>
    [Fact]
    public async Task GetEpisodesAsync_SameSeriesId_UsesCacheNotHttp()
    {
        // Arrange
        var episodesJson = """{ "data": [{ "sonarrEpisodeId": 1, "sonarrSeriesId": 10, "season": 1, "episode": 1 }] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodesJson);

        // Act - Call twice for same series
        var result1 = await _service.GetEpisodesAsync(10);
        var result2 = await _service.GetEpisodesAsync(10);

        // Assert - Only one request
        Assert.Single(_mockHandler.CapturedRequests);
    }

    /// <summary>
    /// Different series should make separate requests (separate cache keys).
    /// </summary>
    [Fact]
    public async Task GetEpisodesAsync_DifferentSeriesId_MakesSeparateRequests()
    {
        // Arrange - Two responses for two different series
        var episodes1Json = """{ "data": [{ "sonarrEpisodeId": 1, "sonarrSeriesId": 10, "season": 1, "episode": 1 }] }""";
        var episodes2Json = """{ "data": [{ "sonarrEpisodeId": 100, "sonarrSeriesId": 20, "season": 1, "episode": 1 }] }""";
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodes1Json);
        _mockHandler.QueueResponse(HttpStatusCode.OK, episodes2Json);

        // Act - Call for different series
        var result1 = await _service.GetEpisodesAsync(10);
        var result2 = await _service.GetEpisodesAsync(20);

        // Assert - Two requests made
        Assert.Equal(2, _mockHandler.CapturedRequests.Count);
        Assert.Equal(1, result1[0].SonarrEpisodeId);
        Assert.Equal(100, result2[0].SonarrEpisodeId);
    }
}
