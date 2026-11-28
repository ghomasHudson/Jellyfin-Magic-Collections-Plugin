using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MagicCollections.Configuration;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("MagicCollections/ArrServerTest")]
public sealed class ArrServerTestController(ArrServerTester tester) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(65_536)]
    public async Task<IActionResult> TestAsync([FromBody] ArrServerTestRequest request, CancellationToken cancellationToken) =>
        Ok(new { Results = await tester.TestAsync(request.ArrServers ?? [], cancellationToken).ConfigureAwait(false) });
}

public sealed record ArrServerTestRequest(ArrServerConfiguration?[]? ArrServers);
