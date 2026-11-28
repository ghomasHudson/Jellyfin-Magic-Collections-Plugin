using System.Text.Json.Serialization;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MagicCollections.Configuration;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("MagicCollections/ArrServerBases")]
public sealed class ArrServerBasesController : ControllerBase
{
    // Dashboard preflight: report a safe row error without receiving API keys or contacting these servers.
    [HttpPost]
    public IActionResult Validate([FromBody] ArrServerBasesRequest request) =>
        Ok(new ArrServerBasesResponse(ArrServerValidation.FindInvalidBase(request.ArrBaseUrls)));

    private sealed record ArrServerBasesResponse(
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ArrBaseError? Error);
}

public sealed record ArrServerBasesRequest(string?[]? ArrBaseUrls);
