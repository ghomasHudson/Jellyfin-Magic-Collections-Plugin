using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.MagicCollections.Configuration;
using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MagicCollections;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Magic Collections";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("3290c5ef-9e20-4cdf-ab34-87e9021bcc34");

    // Keep reading the existing settings after renaming the DLL.
    public override string ConfigurationFileName => "Jellyfin.Plugin.AutoCollections.xml";

    /// <inheritdoc />
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        // Jellyfin calls this for dashboard and direct API saves, before writing the XML.
        if (configuration is PluginConfiguration settings)
        {
            ArrServerValidation.EnsureValid(settings);
            settings.SourceUrls = (settings.SourceUrls ?? []).Select(TraktHandler.CanonicalizeConfiguredUrl).ToArray();
        }

        base.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    internal static string HttpUserAgent { get; } =
        $"MagicCollections/{typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "1.0"}";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
