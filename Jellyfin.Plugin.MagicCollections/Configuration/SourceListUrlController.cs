using Jellyfin.Plugin.MagicCollections.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MagicCollections.Configuration;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("MagicCollections/SourceListUrl")]
public sealed class SourceListUrlController(IEnumerable<ISourceListHandler> handlers) : ControllerBase
{
    [HttpPost]
    public bool Validate([FromBody] SourceListUrlRequest request)
    {
        if (JellyfinQuery.ParseSource(request.Url) is { } local)
        {
            return local.Query is not null;
        }

        if (request.ArrBaseUrls is not { } bases)
        {
            return SourceListHandlerLookup.Find(handlers, request.Url) is not null;
        }

        // The dashboard already has the current draft; do not look up saved *arr credentials or call an *arr API.
        return SourceListHandlerLookup.Find(handlers, request.Url, (handler, url) => handler is ArrHandler arr
            ? arr.SupportsDraft(url, bases)
            : handler.Supports(url)) is not null;
    }
}

public sealed record SourceListUrlRequest(string? Url, string?[]? ArrBaseUrls = null);
