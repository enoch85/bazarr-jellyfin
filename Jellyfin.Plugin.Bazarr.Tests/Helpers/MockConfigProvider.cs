using Jellyfin.Plugin.Bazarr.Services;

namespace Jellyfin.Plugin.Bazarr.Tests.Helpers;

/// <summary>
/// Mock configuration provider for tests.
/// </summary>
public class MockConfigProvider : IBazarrConfigProvider
{
    /// <summary>
    /// Gets or sets the Bazarr URL.
    /// </summary>
    public string BazarrUrl { get; set; } = "http://localhost:6767";

    /// <summary>
    /// Gets or sets the Bazarr API key.
    /// </summary>
    public string BazarrApiKey { get; set; } = "test-api-key";
}
