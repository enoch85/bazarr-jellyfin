using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Bazarr.Api.Models;

/// <summary>
/// Represents a generic Bazarr API response.
/// </summary>
/// <typeparam name="T">The type of data in the response.</typeparam>
public class BazarrResponse<T>
{
    /// <summary>
    /// Gets or sets the data array.
    /// </summary>
    [JsonPropertyName("data")]
#pragma warning disable CA2227 // Collection properties should be read only - needed for JSON deserialization
    public IReadOnlyList<T>? Data { get; set; }
#pragma warning restore CA2227
}
