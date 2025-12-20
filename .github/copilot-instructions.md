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
