using System.Text.Json.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MagicCollections.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    private ArrServerConfiguration[]? _arrServers;
    private ArrServerConfiguration[] _legacyRadarrServers = [];
    private ArrServerConfiguration[] _legacySonarrServers = [];

    public string[] SourceUrls { get; set; } = [];
    public string TraktClientId { get; set; } = string.Empty;
    public ArrServerConfiguration[] ArrServers
    {
        get => _arrServers ?? [.. _legacyRadarrServers, .. _legacySonarrServers];
        set => _arrServers = value ?? [];
    }

    // Legacy XML and dashboard/API input only. A supplied ArrServers list (even empty) wins.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArrServerConfiguration[]? RadarrServers
    {
        get => null;
        set => _legacyRadarrServers = value ?? [];
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArrServerConfiguration[]? SonarrServers
    {
        get => null;
        set => _legacySonarrServers = value ?? [];
    }

    public bool PosterBackgroundImage { get; set; } = true;
    public bool PosterIcon { get; set; }
    public bool PosterListName { get; set; } = true;
    public bool VerboseLogging { get; set; }
}
