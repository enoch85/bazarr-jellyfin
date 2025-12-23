# Copilot Instructions for bazarr-jellyfin

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
- `/api/episodes?seriesid[]={sonarrSeriesId}` returns `sonarrEpisodeId`, `season`, `episode` (NO tvdbId)
- `/api/providers/episodes?episodeid={sonarrEpisodeId}` searches for subtitles


