using System;
using Jellyfin.Plugin.Bazarr.Providers;
using Jellyfin.Plugin.Bazarr.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Bazarr;

/// <summary>
/// Registers plugin services with the DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddMemoryCache();
        serviceCollection.AddSingleton<IBazarrConfigProvider, PluginConfigProvider>();
        serviceCollection.AddHttpClient<IBazarrService, BazarrService>(client =>
        {
            // Bazarr subtitle searches can take a very long time (10-20 minutes)
            // because it queries multiple subtitle providers (OpenSubtitles, Subscene, etc.) in real-time
            // Each provider may take 30-60 seconds, and they're queried sequentially
            // The 25s UI timeout returns early so the user doesn't wait, but background search needs time
            client.Timeout = TimeSpan.FromMinutes(20);
        });

        // Register subtitle handlers
        serviceCollection.AddSingleton<MovieSubtitleHandler>();
        serviceCollection.AddSingleton<EpisodeSubtitleHandler>();

        // Register the subtitle provider so it appears in Jellyfin's subtitle search
        serviceCollection.AddSingleton<ISubtitleProvider, BazarrSubtitleProvider>();
    }
}
