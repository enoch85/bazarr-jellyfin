using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Bazarr.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        BazarrUrl = "http://localhost:6767";
        BazarrApiKey = string.Empty;
        EnableForMovies = true;
        EnableForEpisodes = true;
        SearchTimeoutSeconds = 25;
    }

    /// <summary>
    /// Gets or sets the Bazarr URL.
    /// </summary>
    public string BazarrUrl { get; set; }

    /// <summary>
    /// Gets or sets the Bazarr API key.
    /// </summary>
    public string BazarrApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable subtitle requests for movies.
    /// </summary>
    public bool EnableForMovies { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable subtitle requests for episodes.
    /// </summary>
    public bool EnableForEpisodes { get; set; }

    /// <summary>
    /// Gets or sets the search timeout in seconds.
    /// If a search takes longer than this, a placeholder result is returned and the search continues in the background.
    /// Set to 0 to disable (wait indefinitely). Default is 25 seconds to avoid reverse proxy timeouts.
    /// </summary>
    public int SearchTimeoutSeconds { get; set; }
}
