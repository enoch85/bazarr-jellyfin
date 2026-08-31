# Agent Instructions for bazarr-jellyfin

## Project Overview

This is a Jellyfin plugin that integrates with Bazarr for subtitle management. It allows users to search and download subtitles through Jellyfin's native subtitle search interface.

## Tech Stack

- .NET 9.0
- Jellyfin Plugin SDK
- xUnit for testing

## Release Process

To create a new release, simply tag and push. The GitHub Actions workflow will automatically:
1. Build the plugin
2. Create the ZIP package
3. Update the manifest
4. Create the GitHub release with artifacts

```bash
git tag -a v1.1.5 -m "Description of changes" && git push origin v1.1.5
```

## Key Files

- `Jellyfin.Plugin.Bazarr/` - Main plugin code
- `Jellyfin.Plugin.Bazarr.Tests/` - Unit tests
- `manifest.json` - Jellyfin plugin manifest (auto-updated by release workflow)
- `.github/workflows/release.yaml` - Release automation

## Commits and Pull Requests

- **No AI attribution.** Never add `Co-Authored-By: Claude`, "Generated with Claude Code",
  or any similar trailer, footer or badge to a commit message, PR title, PR body or issue
  comment. Commits are authored by the repository owner.

## API Notes

- Bazarr API uses array notation for some parameters (e.g., `seriesid[]` not `seriesid`)
- Subtitle provider searches can take 1-2 minutes as Bazarr queries multiple providers in real-time

## Design Decisions

### Subtitle Download Exception Pattern (BY DESIGN)

When downloading subtitles, the plugin throws an `InvalidOperationException` after a successful Bazarr download. **This is intentional and correct behavior.**

**Why this is necessary:**
- Jellyfin's `ISubtitleProvider.GetSubtitles()` expects a `SubtitleResponse` with a `Stream` that Jellyfin will save to disk
- Bazarr writes subtitles directly to the media folder (server-side), so there's nothing to stream back
- If we returned an empty stream, Jellyfin would save a 0-byte file
- If we returned `null` stream, Jellyfin would crash in `TrySaveSubtitle`

**The exception message informs the user that the download succeeded and they need to refresh the dialog.**

```csharp
throw new InvalidOperationException(
    "Subtitle downloaded successfully by Bazarr. " +
    "The item is being refreshed - please close and reopen this dialog to see the new subtitle.");
```

**DO NOT "FIX" THIS** - it's the correct workaround for Bazarr's server-side download architecture.

### Episode Subtitle Lookup Architecture

The episode lookup process uses fallback logic due to an ID mismatch between Jellyfin and Bazarr:

**The Problem:**
- Jellyfin's `ISubtitleProvider.Search()` provides `ProviderIds` containing the **episode's** TVDB ID
- Bazarr does NOT store episode-level TVDB IDs - only series-level TVDB IDs
- Bazarr episodes only have: `sonarrEpisodeId`, `season`, `episode` (no external episode IDs)
- We cannot directly look up an episode by its TVDB ID in Bazarr

**Current Solution (fallback chain):**
1. Try TVDB ID as series lookup (rarely works - episode ID ≠ series ID)
2. Try IMDB ID (usually works - IMDB uses series-level IDs for episodes)
3. Try series title matching with season/episode numbers (last resort)

**Why we keep the TVDB lookup:**
- It occasionally works when IDs happen to collide
- No harm in trying (fast, cached)
- Documents the limitation in code comments

**Bazarr API structure:**
- `/api/series` returns `tvdbId`, `imdbId`, `sonarrSeriesId` (series-level only)
- `/api/episodes?seriesid[]={sonarrSeriesId}` returns `sonarrEpisodeId`, `sonarrSeriesId`, `season`, `episode` (NO tvdbId)
- `/api/providers/episodes?episodeid={sonarrEpisodeId}` searches for subtitles

### Movies Match on IMDB Only (BY DESIGN)

Bazarr's `/api/movies` marshals a model that has **no `tmdbId` field** - see `movies_data_model`
in `bazarr/api/movies/movies.py`; `flask_restx.marshal` drops anything not declared there.
Matching a Jellyfin movie to Bazarr therefore only works through `imdbId`, in the search
handler, the controller and the post-download library refresh.

**DO NOT re-add TMDB matching** - it silently never matches, which is what made the
post-download refresh a no-op for every movie.

### Status Rows in Search Results (BY DESIGN)

Jellyfin's `SubtitleManager.SearchSubtitles` catches every exception a provider throws and
returns an empty list, so a failed search is indistinguishable from "no subtitles found".

When a search cannot return results, the plugin therefore returns a single non-downloadable
row built by `SubtitlePlaceholder` (id prefixed `placeholder_`) carrying the reason in
`Comment`. `GetSubtitles` rejects those ids. `ValidateResponseAsync` feeds it Bazarr's own
error text - Bazarr answers failures with a JSON-encoded string body such as
`"All providers are throttled"` (see `bazarr/api/providers/providers_movies.py`).
