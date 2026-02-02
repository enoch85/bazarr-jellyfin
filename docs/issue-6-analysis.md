# Issue #6 Analysis: Non-English UI Language Breaks Subtitle Search

## Issue Summary

When Jellyfin's UI language is set to something other than English, subtitle search fails because:
1. **ID lookup failure**: The TVDB ID passed to Bazarr is the **episode's** TVDB ID, not the **series'** TVDB ID
2. **Title match failure**: Jellyfin passes the **translated** series name instead of the **original** series name

## Logs from Issue

```log
[WRN] No series found in Bazarr for TVDB ID 9213629
[INF] ID lookups failed, trying title match for 'Příběh služebnice' S5E1
[WRN] No series found in Bazarr matching title 'Příběh služebnice'. Available series: ..., The Handmaid's Tale, ...
```

## Root Cause

### Problem 1: Wrong TVDB ID
- Jellyfin's `SubtitleSearchRequest.ProviderIds` contains the **episode's** provider IDs
- Bazarr stores **series-level** TVDB IDs only
- Episode TVDB ID 9213629 ≠ Series TVDB ID

### Problem 2: Translated Title
- Jellyfin's `SubtitleSearchRequest.SeriesName` is populated from `episode.SeriesName`
- `episode.SeriesName` returns the localized name based on UI language
- Bazarr stores titles from Sonarr (original English titles)

## Solution

Access the actual `Episode` and `Series` entities via `ILibraryManager` to get:
1. The series' `OriginalTitle` for title matching fallback
2. The series' `ProviderIds` for ID-based lookups

## Implementation

### Fix 1: Add ILibraryManager to EpisodeSubtitleHandler
Inject `ILibraryManager` to resolve series data from the media path.

### Fix 2: Extract series info from the Episode entity
Use the media path to find the Episode, then access its parent Series for:
- `Series.OriginalTitle` (untranslated name)
- `Series.ProviderIds` (series-level TVDB/IMDB IDs)

### Fix 3: Use series-level IDs in lookup chain
Update the lookup priority to use series-level provider IDs.
