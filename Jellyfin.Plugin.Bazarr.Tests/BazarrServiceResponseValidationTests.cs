using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Bazarr.Services;
using Jellyfin.Plugin.Bazarr.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Bazarr.Tests;

/// <summary>
/// Tests for HTTP response validation in BazarrService.
/// These tests verify proper error handling for authentication proxies, redirects, and HTML responses.
/// </summary>
public class BazarrServiceResponseValidationTests
{
    private const string TestBazarrUrl = "http://bazarr.test";
    private const string TestApiKey = "test-api-key";

    [Fact]
    public async Task TestConnectionAsync_WithHtmlResponse_ReturnsHelpfulError()
    {
        // Arrange: Simulate authentication proxy returning HTML login page
        var htmlContent = @"<!DOCTYPE html>
<html>
<head><title>Login Required</title></head>
<body>
<h1>Please log in</h1>
<form action='/login' method='post'>
<input type='text' name='username' />
<input type='password' name='password' />
<input type='submit' value='Login' />
</form>
</body>
</html>";

        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.OK, htmlContent, "text/html");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("HTML instead of JSON", result.Message);
        Assert.Contains("intercepted", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task TestConnectionAsync_With302Redirect_ReturnsHelpfulError()
    {
        // Arrange: Simulate authentication proxy redirecting to login page
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.Found, "Redirecting...", "text/html");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("redirect", result.Message.ToLowerInvariant());
        Assert.Contains("authentication", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task TestConnectionAsync_With301PermanentRedirect_ReturnsHelpfulError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.MovedPermanently, "", "text/plain");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("redirect", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task TestConnectionAsync_With307TemporaryRedirect_ReturnsHelpfulError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.TemporaryRedirect, "", "text/plain");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("redirect", result.Message.ToLowerInvariant());
        Assert.Contains("intercepting", result.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task GetMoviesAsync_WithHtmlResponse_ThrowsWithHelpfulMessage()
    {
        // Arrange
        var htmlContent = "<html><body>Error 404: Not Found</body></html>";
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/movies", HttpStatusCode.OK, htmlContent, "text/html");

        var service = CreateService(handler);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetMoviesAsync());

        Assert.Contains("HTML instead of JSON", exception.Message);
        Assert.Contains("Incorrect Bazarr URL", exception.Message);
    }

    [Fact]
    public async Task GetSeriesAsync_WithRedirect_ThrowsWithHelpfulMessage()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/series", HttpStatusCode.Found, "", "text/html");

        var service = CreateService(handler);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetSeriesAsync());

        Assert.Contains("redirect", exception.Message);
        Assert.Contains("intercepting", exception.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task GetLanguagesAsync_WithHtmlResponseContainingJsonError_ThrowsWithPreview()
    {
        // Arrange: Simulate response that starts with HTML but might confuse JSON parser
        var htmlContent = @"<html><body>
<script>
  window.location.href = 'https://authentik.example.com/login';
</script>
</body></html>";

        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.OK, htmlContent, "text/html; charset=utf-8");

        var service = CreateService(handler);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetLanguagesAsync());

        Assert.Contains("HTML instead of JSON", exception.Message);
        Assert.Contains("Response preview:", exception.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_WithValidJsonResponse_ReturnsSuccess()
    {
        // Arrange
        var jsonContent = @"[
            {""code2"": ""en"", ""name"": ""English"", ""enabled"": true},
            {""code2"": ""es"", ""name"": ""Spanish"", ""enabled"": true}
        ]";

        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.OK, jsonContent, "application/json");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Connected successfully", result.Message);
        Assert.Contains("2 languages available", result.Message);
    }

    [Fact]
    public async Task GetMoviesAsync_WithValidJsonResponse_ReturnsMovies()
    {
        // Arrange
        var jsonContent = @"{
            ""data"": [
                {""title"": ""Test Movie"", ""radarrId"": 1, ""tmdbId"": 12345}
            ]
        }";

        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/movies", HttpStatusCode.OK, jsonContent, "application/json");

        var service = CreateService(handler);

        // Act
        var movies = await service.GetMoviesAsync();

        // Assert
        Assert.Single(movies);
        Assert.Equal("Test Movie", movies[0].Title);
    }

    [Fact]
    public async Task TestConnectionAsync_With401Unauthorized_ReturnsUnauthorizedError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.Unauthorized, "Unauthorized", "text/plain");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("401", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_With403Forbidden_ReturnsForbiddenError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.Forbidden, "Forbidden", "text/plain");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("403", result.Message);
    }

    [Fact]
    public async Task TestConnectionAsync_With404NotFound_ReturnsNotFoundError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/api/system/languages", HttpStatusCode.NotFound, "Not Found", "text/plain");

        var service = CreateService(handler);

        // Act
        var result = await service.TestConnectionAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("404", result.Message);
    }

    private static BazarrService CreateService(MockHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(TestBazarrUrl)
        };

        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = new Mock<ILogger<BazarrService>>().Object;
        var configProvider = new MockConfigProvider(TestBazarrUrl, TestApiKey);

        return new BazarrService(httpClient, cache, logger, configProvider);
    }
}
