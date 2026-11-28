using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Jellyfin.Plugin.MagicCollections.Sync;

internal static class ArrTagReader
{
    private const string StatusContext = "*arr system status";

    public static async Task<ArrServiceDefinition> DetectAsync(HttpClient http, Uri baseUrl, string apiKey,
        CancellationToken cancellationToken)
    {
        var supported = $"{ArrServiceDefinition.Radarr.MinimumRequirement} or {ArrServiceDefinition.Sonarr.MinimumRequirement}";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var status = await ReadJsonAsync(http, baseUrl, apiKey, "api/v3/system/status", StatusContext, deadline.Token,
                maxResponseBytes: 64 * 1024)
                .ConfigureAwait(false);
            var root = status.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetUniqueProperty(root, "appName", out var appName) ||
                appName.ValueKind != JsonValueKind.String ||
                ReadString(appName) is not { Length: > 0 } name ||
                !TryGetUniqueProperty(root, "version", out var versionField) ||
                versionField.ValueKind != JsonValueKind.String ||
                ReadString(versionField) is not { } rawVersion ||
                rawVersion.Any(c => c != '.' && c is not (>= '0' and <= '9')) ||
                !Version.TryParse(rawVersion, out var version))
            {
                throw new JsonException();
            }

            var definition = name switch
            {
                "Radarr" => ArrServiceDefinition.Radarr,
                "Sonarr" => ArrServiceDefinition.Sonarr,
                _ => throw ReadError(baseUrl, StatusContext, $"Unsupported application. Requires {supported}.")
            };
            if (version < definition.MinimumVersion)
            {
                throw ReadError(baseUrl, StatusContext, $"Unsupported version. Requires {definition.MinimumRequirement}.");
            }
            return definition;
        }
        catch (JsonException)
        {
            throw ReadError(baseUrl, StatusContext, $"Invalid or incomplete system status response. Requires {supported}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ReadError(baseUrl, StatusContext, "API request timed out.");
        }
    }

    public static async Task<SourceList> ReadAsync(HttpClient http, ArrTagSource source,
        ArrServiceDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            using var tags = await ReadArrayAsync("api/v3/tag").ConfigureAwait(false);
            int? tagId = null;
            var tagIds = new HashSet<int>();
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tag in tags.RootElement.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.Object || !TryGetUniqueProperty(tag, "id", out var id) ||
                    !id.TryReadPositiveInt(out var value) || !TryGetUniqueProperty(tag, "label", out var property) ||
                    property.ValueKind != JsonValueKind.String || ReadString(property) is not { } label ||
                    string.IsNullOrWhiteSpace(label) || label.Any(char.IsControl) || !tagIds.Add(value) || !labels.Add(label))
                {
                    throw new JsonException();
                }

                if (label == source.Label)
                {
                    tagId = value;
                }
            }
            if (tagId is null)
            {
                throw ReadError(source.BaseUrl, definition.Name, "Tag does not exist.");
            }

            using var media = await ReadArrayAsync(definition.MediaEndpoint).ConfigureAwait(false);
            var items = new List<MediaIdentity>();
            foreach (var row in media.RootElement.EnumerateArray())
            {
                if (HasTag(row, tagId.Value))
                {
                    var providers = new Dictionary<string, string>();
                    var hasPrimary = false;
                    foreach (var field in definition.ProviderFields)
                    {
                        TryGetUniqueProperty(row, field.Property, out var value);
                        var (present, id) = ReadProviderId(value, field.Kind);
                        hasPrimary |= field.Primary && present;
                        if (id is not null)
                        {
                            providers.Add(field.Provider, id);
                        }
                    }
                    if (!hasPrimary && providers.Count == 0)
                    {
                        throw new JsonException();
                    }
                    if (providers.Count != 0)
                    {
                        items.Add(new(definition.MediaType, providers));
                    }
                }
            }
            return new(source.Label, items) { ArrServiceTag = definition.Name };
        }
        catch (JsonException)
        {
            throw ReadError(source.BaseUrl, definition.Name, "Invalid or incomplete API response.");
        }

        async Task<JsonDocument> ReadArrayAsync(string endpoint)
        {
            var data = await ReadJsonAsync(http, source.BaseUrl, source.ApiKey, endpoint, definition.Name, cancellationToken)
                .ConfigureAwait(false);
            if (data.RootElement.ValueKind != JsonValueKind.Array)
            {
                data.Dispose();
                throw new JsonException();
            }
            return data;
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpClient http, Uri baseUrl, string apiKey, string endpoint,
        string context, CancellationToken cancellationToken, int? maxResponseBytes = null)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUrl, endpoint));
            request.Headers.Add("X-Api-Key", apiKey);
            using var response = await http.SendAsync(request, maxResponseBytes is null
                ? HttpCompletionOption.ResponseContentRead
                : HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw ReadError(baseUrl, context,
                    FormattableString.Invariant($"API returned HTTP {(int)response.StatusCode}."), response.StatusCode);
            }
            if (maxResponseBytes is { } limit)
            {
                await response.Content.LoadIntoBufferAsync(limit, cancellationToken).ConfigureAwait(false);
            }
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw ReadError(baseUrl, context, "API request failed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ReadError(baseUrl, context, "API request timed out.");
        }
    }

    private static SourceListReadException ReadError(Uri baseUrl, string context, string reason,
        HttpStatusCode? status = null) => new(baseUrl, $"{context}: {reason}") { StatusCode = status };

    // JsonDocument parses escapes lazily; extracting a string with invalid Unicode can throw
    // InvalidOperationException rather than JsonException.
    private static string? ReadString(JsonElement value) => ReadJsonValue(value, element => element.GetString());

    private static string? ReadImdbId(JsonElement value) =>
        ReadJsonValue(value, element => ImdbId.ReadOptional(element, minimumDigits: 7));

    private static string? ReadJsonValue(JsonElement value, Func<JsonElement, string?> read)
    {
        try
        {
            return read(value);
        }
        catch (InvalidOperationException)
        {
            throw new JsonException();
        }
    }

    private static bool HasTag(JsonElement row, int tagId)
    {
        if (row.ValueKind != JsonValueKind.Object || !TryGetUniqueProperty(row, "tags", out var tags))
        {
            throw new JsonException();
        }
        if (tags.ValueKind == JsonValueKind.Null)
        {
            return false;
        }
        if (tags.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException();
        }

        var match = false;
        foreach (var tag in tags.EnumerateArray())
        {
            if (!tag.TryReadPositiveInt(out var id))
            {
                throw new JsonException();
            }
            match |= id == tagId;
        }
        return match;
    }

    private static (bool Present, string? Id) ReadProviderId(JsonElement value, ArrProviderIdKind kind)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return (false, null);
        }
        if (kind == ArrProviderIdKind.Imdb)
        {
            return (true, ReadImdbId(value));
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var id) || id < 0)
        {
            throw new JsonException();
        }
        // Zero is an explicitly unavailable ID, distinct from a missing field.
        return (true, id > 0 ? id.ToString(CultureInfo.InvariantCulture) : null);
    }

    // JsonElement.TryGetProperty silently picks the last value. Reject ambiguous fields used for detection or membership.
    private static bool TryGetUniqueProperty(JsonElement row, string name, out JsonElement value)
    {
        value = default;
        foreach (var property in row.EnumerateObject())
        {
            string propertyName;
            try
            {
                propertyName = property.Name;
            }
            catch (InvalidOperationException)
            {
                throw new JsonException();
            }
            if (propertyName != name)
            {
                continue;
            }
            if (value.ValueKind != JsonValueKind.Undefined)
            {
                throw new JsonException();
            }
            value = property.Value;
        }
        return value.ValueKind != JsonValueKind.Undefined;
    }

    private static bool TryReadPositiveInt(this JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) && value > 0;
    }
}
