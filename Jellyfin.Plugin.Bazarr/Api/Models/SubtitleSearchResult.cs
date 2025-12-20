using System.Collections.Generic;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Result of a subtitle search operation.
/// </summary>
public class SubtitleSearchResult
{
    /// <summary>
    /// Gets or sets the list of subtitles found.
    /// </summary>
    public IReadOnlyList<SubtitleOption> Subtitles { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a search is still in progress.
    /// </summary>
    public bool SearchInProgress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether these are cached results.
    /// </summary>
    public bool FromCache { get; set; }
}
