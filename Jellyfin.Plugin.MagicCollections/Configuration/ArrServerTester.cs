using System.Text.Json.Serialization;
using Jellyfin.Plugin.MagicCollections.Sync;

namespace Jellyfin.Plugin.MagicCollections.Configuration;

public sealed class ArrServerTester
{
    private readonly HttpClient _http;

    internal ArrServerTester(HttpClient http) => _http = http;

    internal async Task<ArrServerTestResult[]> TestAsync(ArrServerConfiguration?[] servers, CancellationToken cancellationToken)
    {
        var targets = new (Uri? BaseUrl, string? Key, string? Error)[servers.Length];
        var seen = new HashSet<(string Origin, string Path)>();
        var hasInvalidRows = false;
        for (var index = 0; index < servers.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var server = servers[index];
            var error = ArrServerValidation.CheckBase(server?.BaseUrl, seen, out var location);
            var key = ArrTagSource.ReadApiKey(server?.ApiKey);
            if (error is null && key is not null)
            {
                targets[index] = (new(location.Origin + location.Path), key, null);
            }
            else
            {
                hasInvalidRows = true;
                targets[index] = (null, null, error ?? ArrServerValidation.InvalidKey);
            }
        }

        if (hasInvalidRows)
        {
            return targets.Select((target, index) =>
                new ArrServerTestResult(index, null, target.Error ?? "Not tested. Correct the other server rows and try again."))
                .ToArray();
        }

        // Check sequentially, with a short per-server timeout and a ceiling for the entire batch.
        using var batchDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        batchDeadline.CancelAfter(TimeSpan.FromSeconds(30));
        var results = new ArrServerTestResult[targets.Length];
        for (var index = 0; index < targets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (batchDeadline.IsCancellationRequested)
            {
                results[index] = new(index, null, "Not tested. The server test exceeded its time limit.");
                continue;
            }
            var (baseUrl, key, _) = targets[index];
            try
            {
                var definition = await ArrTagReader.DetectAsync(_http, baseUrl!, key!, batchDeadline.Token).ConfigureAwait(false);
                results[index] = new(index, definition.Name, "Connected successfully.");
            }
            catch (SourceListReadException ex)
            {
                // Reason contains only fixed text and a numeric HTTP status; Message contains the base URL.
                results[index] = new(index, null, ex.Reason);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                results[index] = new(index, null, "The system status request timed out.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }
}

internal sealed record ArrServerTestResult(int Index,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Service, string Message);
