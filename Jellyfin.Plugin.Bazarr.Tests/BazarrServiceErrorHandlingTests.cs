using System.Net;
using Jellyfin.Plugin.Bazarr.Services;
using Jellyfin.Plugin.Bazarr.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace Jellyfin.Plugin.Bazarr.Tests;

/// <summary>
/// Tests for error handling in BazarrService.
/// These tests ensure graceful degradation when Bazarr is unreachable or returns errors.
/// Without proper error handling:
/// - Plugin crashes could bring down Jellyfin
/// - Uncaught exceptions would show cryptic errors to users
/// </summary>
public class BazarrServiceErrorHandlingTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly Mock<ILogger<BazarrService>> _loggerMock;
    private readonly MockConfigProvider _configProvider;
    private readonly BazarrService _service;

    public BazarrServiceErrorHandlingTests()
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
    /// CRITICAL: TestConnectionAsync must catch exceptions and return a friendly result.
    /// This is used by the config page - an uncaught exception would break the UI.
    /// </summary>
    [Fact]
    public async Task TestConnectionAsync_WhenBazarrUnreachable_ReturnsFriendlyError()
    {
        // Arrange - Simulate network failure
        _mockHandler.QueueException(new HttpRequestException("Connection refused"));

        // Act
        var result = await _service.TestConnectionAsync();

        // Assert - Should NOT throw, should return error result
        Assert.False(result.Success);
        Assert.Contains("Connection failed", result.Message);
    }

    /// <summary>
    /// TestConnectionAsync must handle unexpected exceptions too.
    /// </summary>
    [Fact]
    public async Task TestConnectionAsync_WhenUnexpectedError_ReturnsErrorResult()
    {
        // Arrange - Simulate unexpected error (e.g., JSON parse failure)
        _mockHandler.QueueException(new InvalidOperationException("Something unexpected"));

        // Act
        var result = await _service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Unexpected error", result.Message);
    }

    /// <summary>
    /// When connection succeeds, TestConnectionAsync should return success with language count.
    /// </summary>
    [Fact]
    public async Task TestConnectionAsync_WhenSuccess_ReturnsSuccessWithLanguageCount()
    {
        // Arrange - Languages endpoint returns a direct array, not wrapped in { "data": [...] }
        var languagesJson = """
        [
            { "code2": "en", "code3": "eng", "name": "English", "enabled": true },
            { "code2": "fr", "code3": "fra", "name": "French", "enabled": true },
            { "code2": "de", "code3": "deu", "name": "German", "enabled": false }
        ]
        """;
        _mockHandler.QueueResponse(HttpStatusCode.OK, languagesJson);

        // Act
        var result = await _service.TestConnectionAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("3", result.Message); // Should mention 3 languages
    }

    /// <summary>
    /// When Bazarr returns 401, GetMoviesAsync should throw with clear message.
    /// This helps users diagnose API key issues.
    /// </summary>
    [Fact]
    public async Task GetMoviesAsync_WhenUnauthorized_ThrowsWithStatusCode()
    {
        // Arrange
        _mockHandler.QueueResponse(HttpStatusCode.Unauthorized, "");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => _service.GetMoviesAsync());
    }

    /// <summary>
    /// When Bazarr returns empty data array, should return empty list not crash.
    /// </summary>
    [Fact]
    public async Task GetMoviesAsync_WhenEmptyData_ReturnsEmptyList()
    {
        // Arrange
        _mockHandler.QueueResponse(HttpStatusCode.OK, """{ "data": [] }""");

        // Act
        var result = await _service.GetMoviesAsync();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When Bazarr returns null data, should return empty list not crash.
    /// This handles edge case of malformed response.
    /// </summary>
    [Fact]
    public async Task GetMoviesAsync_WhenNullData_ReturnsEmptyList()
    {
        // Arrange - Response with no data property
        _mockHandler.QueueResponse(HttpStatusCode.OK, """{ }""");

        // Act
        var result = await _service.GetMoviesAsync();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// Download returning 500 should return false, not throw.
    /// </summary>
    [Fact]
    public async Task DownloadMovieSubtitleAsync_WhenServerError_ReturnsFalse()
    {
        // Arrange
        _mockHandler.QueueResponse(HttpStatusCode.InternalServerError, "");

        var request = new Api.Models.DownloadRequest
        {
            RadarrId = 1,
            Provider = "opensubtitles",
            Subtitle = "encoded_subtitle_data",
            Hi = "False",
            Forced = "False"
        };

        // Act
        var result = await _service.DownloadMovieSubtitleAsync(request);

        // Assert - Should return false, not throw
        Assert.False(result);
    }

    /// <summary>
    /// Download returning 200 should return true.
    /// </summary>
    [Fact]
    public async Task DownloadMovieSubtitleAsync_WhenSuccess_ReturnsTrue()
    {
        // Arrange
        _mockHandler.QueueResponse(HttpStatusCode.OK, "");

        var request = new Api.Models.DownloadRequest
        {
            RadarrId = 1,
            Provider = "opensubtitles",
            Subtitle = "encoded_subtitle_data",
            Hi = "False",
            Forced = "False"
        };

        // Act
        var result = await _service.DownloadMovieSubtitleAsync(request);

        // Assert
        Assert.True(result);
    }
}
