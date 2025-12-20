namespace Jellyfin.Plugin.Bazarr.Services;

/// <summary>
/// Provides Bazarr configuration settings.
/// </summary>
public interface IBazarrConfigProvider
{
    /// <summary>
    /// Gets the Bazarr server URL.
    /// </summary>
    string BazarrUrl { get; }

    /// <summary>
    /// Gets the Bazarr API key.
    /// </summary>
    string BazarrApiKey { get; }
}
