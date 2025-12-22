using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Bazarr.Api.Models;

namespace Jellyfin.Plugin.Bazarr.Providers;

/// <summary>
/// Helper class for language code mapping and filtering.
/// </summary>
public static class SubtitleLanguageHelper
{
    /// <summary>
    /// Mapping of 2-letter ISO 639-1 codes to their 3-letter ISO 639-2 equivalents.
    /// Used for language filtering since Bazarr uses 2-letter codes and Jellyfin may use either.
    /// </summary>
    private static readonly Dictionary<string, string[]> LanguageMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        { "en", ["eng", "english"] },
        { "es", ["spa", "spanish"] },
        { "fr", ["fra", "fre", "french"] },
        { "de", ["deu", "ger", "german"] },
        { "it", ["ita", "italian"] },
        { "pt", ["por", "portuguese"] },
        { "nl", ["nld", "dut", "dutch"] },
        { "pl", ["pol", "polish"] },
        { "ru", ["rus", "russian"] },
        { "ja", ["jpn", "japanese"] },
        { "zh", ["zho", "chi", "chinese"] },
        { "ko", ["kor", "korean"] },
        { "ar", ["ara", "arabic"] },
        { "sv", ["swe", "swedish"] },
        { "da", ["dan", "danish"] },
        { "no", ["nor", "norwegian"] },
        { "fi", ["fin", "finnish"] },
        { "he", ["heb", "hebrew"] },
        { "tr", ["tur", "turkish"] },
        { "el", ["ell", "gre", "greek"] },
        { "cs", ["ces", "cze", "czech"] },
        { "hu", ["hun", "hungarian"] },
        { "ro", ["ron", "rum", "romanian"] },
        { "th", ["tha", "thai"] },
        { "vi", ["vie", "vietnamese"] },
        { "id", ["ind", "indonesian"] },
        { "uk", ["ukr", "ukrainian"] },
        { "bg", ["bul", "bulgarian"] },
        { "hr", ["hrv", "croatian"] },
        { "sk", ["slk", "slo", "slovak"] },
        { "sl", ["slv", "slovenian"] },
    };

    /// <summary>
    /// Gets the appropriate language code from the request parameters.
    /// </summary>
    /// <param name="language">The full language string.</param>
    /// <param name="twoLetterCode">The two-letter ISO code.</param>
    /// <returns>The language code to use for searching.</returns>
    public static string GetLanguageCode(string? language, string? twoLetterCode)
    {
        // Prefer the two-letter code if available
        if (!string.IsNullOrEmpty(twoLetterCode))
        {
            return twoLetterCode;
        }

        if (!string.IsNullOrEmpty(language))
        {
            return language;
        }

        return "en";
    }

    /// <summary>
    /// Gets the subtitle format, handling Bazarr's quirky "False" string.
    /// Returns uppercase format names (SRT, ASS, etc.) for better readability in the UI.
    /// </summary>
    /// <param name="originalFormat">The original format from Bazarr.</param>
    /// <returns>A valid subtitle format string in uppercase.</returns>
    public static string GetSubtitleFormat(string? originalFormat)
    {
        // Bazarr returns "False" as a string when format is unknown, or null
        if (string.IsNullOrEmpty(originalFormat) ||
            originalFormat.Equals("False", StringComparison.OrdinalIgnoreCase) ||
            originalFormat.Equals("True", StringComparison.OrdinalIgnoreCase))
        {
            return "SRT"; // Default to SRT
        }

        // Return uppercase format names for display (SRT, ASS, VTT, etc.)
        // Jellyfin's MIME type lookup is case-insensitive, so this works correctly
        return originalFormat.ToUpperInvariant();
    }

    /// <summary>
    /// Formats subtitle comment with provider, score percentage, and uploader info.
    /// </summary>
    /// <param name="subtitle">The subtitle option from Bazarr.</param>
    /// <returns>A formatted comment string for display.</returns>
    public static string FormatSubtitleComment(Api.Models.SubtitleOption subtitle)
    {
        var parts = new List<string> { subtitle.Provider };

        // Add score with percentage
        parts.Add($"Score: {subtitle.Score}%");

        // Add uploader if available (helps identify quality - some uploaders like os-auto may be machine-generated)
        if (!string.IsNullOrWhiteSpace(subtitle.Uploader))
        {
            parts.Add($"by {subtitle.Uploader}");
        }

        return string.Join(" - ", parts);
    }

    /// <summary>
    /// Filters subtitles by the requested language, handling various code formats.
    /// </summary>
    /// <param name="subtitles">The list of subtitle options.</param>
    /// <param name="requestedLanguage">The requested language code.</param>
    /// <returns>Filtered subtitles matching the language.</returns>
    public static IEnumerable<SubtitleOption> FilterByLanguage(
        IReadOnlyList<SubtitleOption> subtitles,
        string requestedLanguage)
    {
        // Bazarr returns language codes that can be:
        // - 2-letter codes: "en", "es", "fr", "pt"
        // - Regional variants: "pt-BR", "zh-CN", "zh-TW" (when subtitle provider includes country)
        //
        // Jellyfin may request:
        // - 2-letter codes: "pt", "zh"
        // - Regional variants: "pt-br", "zh-cn"
        // - 3-letter codes: "por", "zho"
        //
        // We need to match:
        // 1. Exact match (case-insensitive): "pt-br" == "pt-BR"
        // 2. Base language match: request "pt-br" matches subtitle "pt"
        // 3. Reverse base match: request "pt" matches subtitle "pt-BR"
        // 4. 3-letter to 2-letter mapping: request "por" matches subtitle "pt"

        // Extract base language from regional variant (e.g., "pt-br" -> "pt", "zh-cn" -> "zh")
        var baseRequestedLanguage = GetBaseLanguage(requestedLanguage);

        return subtitles.Where(s =>
        {
            var subtitleLanguage = s.Language;
            var baseSubtitleLanguage = GetBaseLanguage(subtitleLanguage);

            // Case 1: Exact match (handles "pt-br" == "pt-BR")
            if (subtitleLanguage.Equals(requestedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Case 2: Base language of request matches subtitle (handles "pt-br" request matching "pt" subtitle)
            if (subtitleLanguage.Equals(baseRequestedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Case 3: Request matches base of subtitle (handles "pt" request matching "pt-BR" subtitle)
            if (baseSubtitleLanguage.Equals(requestedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Case 4: Both base languages match (handles "pt-br" request matching "pt-BR" subtitle via "pt" == "pt")
            if (baseSubtitleLanguage.Equals(baseRequestedLanguage, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Case 5: 3-letter to 2-letter code mapping (handles "por" request matching "pt" subtitle)
            if (MatchesLanguageCode(subtitleLanguage, requestedLanguage) ||
                MatchesLanguageCode(subtitleLanguage, baseRequestedLanguage) ||
                MatchesLanguageCode(baseSubtitleLanguage, requestedLanguage) ||
                MatchesLanguageCode(baseSubtitleLanguage, baseRequestedLanguage))
            {
                return true;
            }

            return false;
        });
    }

    /// <summary>
    /// Gets the base language from a regional variant.
    /// </summary>
    /// <param name="language">The language code (e.g., "pt-br", "zh-cn").</param>
    /// <returns>The base language code (e.g., "pt", "zh").</returns>
    public static string GetBaseLanguage(string language)
    {
        // Handle regional variants like "pt-br", "pt-pt", "zh-cn", "zh-tw"
        if (string.IsNullOrEmpty(language))
        {
            return language;
        }

        var dashIndex = language.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex > 0)
        {
            return language.Substring(0, dashIndex);
        }

        var underscoreIndex = language.IndexOf('_', StringComparison.Ordinal);
        if (underscoreIndex > 0)
        {
            return language.Substring(0, underscoreIndex);
        }

        return language;
    }

    private static bool MatchesLanguageCode(string subtitleLang, string requestedLang)
    {
        // Check if the subtitle language maps to the requested language
        if (LanguageMappings.TryGetValue(subtitleLang, out var equivalents))
        {
            if (equivalents.Any(e => e.Equals(requestedLang, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Check reverse: if requested is 2-letter and subtitle matches any equivalent
        foreach (var kvp in LanguageMappings)
        {
            if (kvp.Key.Equals(requestedLang, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value.Any(e => e.Equals(subtitleLang, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
