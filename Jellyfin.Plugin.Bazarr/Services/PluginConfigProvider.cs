using Jellyfin.Plugin.Bazarr.Configuration;

namespace Jellyfin.Plugin.Bazarr.Services;

/// <summary>
/// Provides Bazarr configuration from the plugin instance.
/// </summary>
public class PluginConfigProvider : IBazarrConfigProvider
{
    /// <inheritdoc />
    public string BazarrUrl => Plugin.Instance?.Configuration?.BazarrUrl ?? string.Empty;

    /// <inheritdoc />
    public string BazarrApiKey => Plugin.Instance?.Configuration?.BazarrApiKey ?? string.Empty;
}
