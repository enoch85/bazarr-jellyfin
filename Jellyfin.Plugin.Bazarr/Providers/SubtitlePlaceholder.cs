using System.Collections.Generic;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Bazarr.Providers;

/// <summary>
/// Builds the status rows shown in Jellyfin's subtitle list when there is no result to offer.
/// These are not downloadable - <see cref="IdPrefix"/> identifies them.
/// </summary>
public static class SubtitlePlaceholder
{
    /// <summary>
    /// Prefix marking a status row rather than a real subtitle.
    /// </summary>
    public const string IdPrefix = "placeholder_";

    /// <summary>
    /// Row shown while a Bazarr search continues in the background.
    /// </summary>
    /// <returns>A single-item result set.</returns>
    public static IEnumerable<RemoteSubtitleInfo> SearchInProgress() => Create(
        "in_progress",
        "Search in progress - results typically ready in 5-15 minutes",
        "Bazarr is searching multiple providers in the background. Click 'Search' again later to see cached results.");

    /// <summary>
    /// Row shown when a Bazarr search could not be completed.
    /// </summary>
    /// <param name="reason">The failure reason to show the user.</param>
    /// <returns>A single-item result set.</returns>
    public static IEnumerable<RemoteSubtitleInfo> SearchFailed(string reason) => Create(
        "failed",
        "Search failed",
        reason);

    private static IEnumerable<RemoteSubtitleInfo> Create(string id, string name, string comment) =>
    [
        new RemoteSubtitleInfo
        {
            Id = IdPrefix + id,
            Name = name,
            ProviderName = "Bazarr",
            Comment = comment
        }
    ];
}
