using Jellyfin.Plugin.MagicCollections;
using Jellyfin.Plugin.MagicCollections.Configuration;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MagicCollections.Tests;

internal static class PluginTestServices
{
    public static ServiceProvider BuildProvider(PluginConfiguration? configuration = null, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        var dataPath = Path.Combine(Path.GetTempPath(), $"magic-collections-tests-{Guid.NewGuid():N}");
        services.AddSingleton<IApplicationPaths>(new TestApplicationPaths(dataPath));
        new ServiceRegistrator().RegisterServices(services, null!);
        // Do not query Plugin.Instance while another test is saving its own plugin settings.
        var testConfiguration = configuration ?? new();
        services.AddSingleton<Func<PluginConfiguration>>(() => testConfiguration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    internal sealed class TestApplicationPaths(string dataPath) : IApplicationPaths
    {
        public string DataPath => dataPath;
        public string ProgramDataPath => throw new NotSupportedException();
        public string WebPath => throw new NotSupportedException();
        public string ProgramSystemPath => throw new NotSupportedException();
        public string ImageCachePath => throw new NotSupportedException();
        public string PluginsPath => Path.Combine(DataPath, "plugins");
        public string PluginConfigurationsPath => Path.Combine(PluginsPath, "configurations");
        public string LogDirectoryPath => throw new NotSupportedException();
        public string ConfigurationDirectoryPath => throw new NotSupportedException();
        public string SystemConfigurationFilePath => throw new NotSupportedException();
        public string CachePath => throw new NotSupportedException();
        public string TempDirectory => throw new NotSupportedException();
        public string VirtualDataPath => throw new NotSupportedException();
        public string TrickplayPath => throw new NotSupportedException();
        public string BackupPath => throw new NotSupportedException();
        public void MakeSanityCheckOrThrow() => throw new NotSupportedException();
        public void CreateAndCheckMarker(string path, string markerName, bool recursive) => throw new NotSupportedException();
    }
}
