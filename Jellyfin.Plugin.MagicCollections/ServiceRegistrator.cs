using System.Net;
using Jellyfin.Plugin.MagicCollections.Configuration;
using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MagicCollections;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Use a dedicated client without replacing Jellyfin's own HTTP services.
        serviceCollection.AddSingleton(_ => new SourceListHttpClient());
        serviceCollection.AddSingleton(services => new ArrServerTester(services.GetRequiredService<SourceListHttpClient>()));
        serviceCollection.AddSingleton<ISourceListHandler>(services =>
            new MdblistHandler(services.GetRequiredService<SourceListHttpClient>()));
        serviceCollection.AddSingleton<ISourceListHandler>(services =>
            new PopularMoviesHandler(services.GetRequiredService<SourceListHttpClient>(), TimeProvider.System,
                services.GetRequiredService<IApplicationPaths>().DataPath));
        serviceCollection.AddSingleton(services =>
            new LetterboxdHandler(services.GetRequiredService<SourceListHttpClient>(),
                services.GetRequiredService<IApplicationPaths>().DataPath));
        serviceCollection.AddSingleton<ISourceListHandler>(services => services.GetRequiredService<LetterboxdHandler>());
        serviceCollection.AddSingleton(services =>
            new CollectionImageClient(services.GetRequiredService<SourceListHttpClient>()));
        serviceCollection.AddSingleton<ISourceListHandler>(services =>
            new BfiHandler(services.GetRequiredService<SourceListHttpClient>()));
        serviceCollection.AddSingleton<Func<PluginConfiguration>>(() => Plugin.Instance?.Configuration ?? new PluginConfiguration());
        serviceCollection.AddSingleton(services => new TraktConnection(
            services.GetRequiredService<SourceListHttpClient>(), services.GetRequiredService<Func<PluginConfiguration>>(),
            services.GetRequiredService<IApplicationPaths>().DataPath, TimeProvider.System));
        serviceCollection.AddSingleton<ISourceListHandler>(services =>
            new TraktHandler(services.GetRequiredService<SourceListHttpClient>(),
                services.GetRequiredService<Func<PluginConfiguration>>(), services.GetRequiredService<TraktConnection>()));
        serviceCollection.AddSingleton<ISourceListHandler>(services =>
        {
            var getConfiguration = services.GetRequiredService<Func<PluginConfiguration>>();
            return new ArrHandler(services.GetRequiredService<SourceListHttpClient>(), () => getConfiguration().ArrServers);
        });
        serviceCollection.AddSingleton<ICollectionLibrary, JellyfinCollectionLibrary>();
        serviceCollection.AddSingleton<JellyfinQueryHandler>();
        serviceCollection.AddSingleton(services => new CollectionSync(
            services.GetServices<ISourceListHandler>(), services.GetRequiredService<ICollectionLibrary>(),
            services.GetRequiredService<JellyfinQueryHandler>()));
    }

    private sealed class SourceListHttpClient() : HttpClient(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All
    });
}
