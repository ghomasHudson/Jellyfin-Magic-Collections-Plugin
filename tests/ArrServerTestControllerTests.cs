using System.Net;
using System.Text.Json;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.MagicCollections.Configuration;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MagicCollections.Tests;

public sealed class ArrServerTestControllerTests
{
    private const string First = "https://first.example.invalid/Proxy";
    private const string Second = "http://second.example.invalid:8989";
    private const string FirstKey = "private-test-key-1";
    private const string SecondKey = "private-test-key-2";
    private const string Status = "/api/v3/system/status";

    [Fact]
    public async Task Testing_draft_settings_checks_only_status_and_returns_rows_in_order()
    {
        var requests = new List<(string? Url, string Key)>();
        var responses = new RecordedHttpResponses(request => requests.Add((request.RequestUri?.AbsoluteUri,
            Assert.Single(request.Headers.GetValues("X-Api-Key")))));
        responses.RecordBody(First + Status, """{"appName":"Sonarr","version":"4.0.0.748","instanceName":"Radarr"}""");
        responses.RecordBody(Second + Status, """{"appName":"Radarr","version":"4.0.5.5981","urlBase":"https://other.example.invalid"}""");
        using var http = new HttpClient(responses);

        var result = await TestAsync(http, TwoServers());

        var rows = result.GetProperty("Results").EnumerateArray().ToArray();
        Assert.Equal([(0, "Sonarr"), (1, "Radarr")], rows.Select(row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("Message").GetString()));
            return (row.GetProperty("Index").GetInt32(), row.GetProperty("Service").GetString());
        }));
        Assert.Equal([(First + Status, FirstKey), (Second + Status, SecondKey)], requests);
        Assert.DoesNotContain("private-test-key", result.GetRawText());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "HTTP 401")]
    [InlineData(HttpStatusCode.Redirect, "HTTP 302")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "HTTP 503")]
    public async Task HTTP_failures_are_safe_row_errors_and_do_not_prevent_checking_the_next_server(
        HttpStatusCode status, string problem)
    {
        var responses = new RecordedHttpResponses();
        responses.Record(First + Status, () => new(status)
        {
            Content = new StringContent(FirstKey),
            Headers = { Location = new("https://other.example.invalid/" + FirstKey) }
        });
        responses.RecordBody(Second + Status, """{"appName":"Sonarr","version":"4.0"}""");
        using var http = new HttpClient(responses);

        var result = await TestAsync(http, TwoServers());

        Assert.Collection(result.GetProperty("Results").EnumerateArray(), row => AssertError(row, 0, problem),
            row => Assert.Equal("Sonarr", row.GetProperty("Service").GetString()));
        Assert.Equal([First + Status, Second + Status], responses.Requests);
        Assert.DoesNotContain("private-test-key", result.GetRawText());
    }

    [Theory]
    [InlineData("""{"appName":"private-test-key-1","version":"6.0"}""", "Unsupported application")]
    [InlineData("""{"appName":"Radarr","version":"4.0.4.5922"}""", "Radarr 4.0.5 or newer")]
    [InlineData("""{"appName":"Sonarr","version":"3.0.10.1567"}""", "Sonarr 4.0 or newer")]
    [InlineData("""{"instanceName":"Sonarr","version":"3.0.5.1144"}""", "Invalid or incomplete system status")]
    [InlineData("""{"appName":"Radarr","version":"private-test-key-1"}""", "Invalid or incomplete system status")]
    public async Task The_draft_test_uses_the_same_application_and_version_checks_as_sync(string status, string problem)
    {
        var responses = new RecordedHttpResponses();
        responses.RecordBody(First + Status, status);
        using var http = new HttpClient(responses);

        var result = await TestAsync(http, [new() { BaseUrl = First, ApiKey = FirstKey }]);

        AssertError(Assert.Single(result.GetProperty("Results").EnumerateArray()), 0, problem);
        Assert.Equal([First + Status], responses.Requests);
        Assert.DoesNotContain(FirstKey, result.GetRawText());
    }

    [Fact]
    public async Task A_batch_with_any_invalid_rows_never_dispatches_requests()
    {
        var responses = new RecordedHttpResponses();
        using var http = new HttpClient(responses);
        var result = await TestAsync(http,
        [
            new() { BaseUrl = First, ApiKey = FirstKey },
            new() { BaseUrl = "https://FIRST.example.invalid:443/Proxy/", ApiKey = SecondKey },
            new() { BaseUrl = Second, ApiKey = "invalid\r\nkey" },
            new() { BaseUrl = Second + "?apikey=" + SecondKey, ApiKey = SecondKey },
            null,
            new() { BaseUrl = "https://example.invalid", ApiKey = null }
        ]);

        Assert.Collection(result.GetProperty("Results").EnumerateArray(),
            row => AssertError(row, 0, "Not tested"),
            row => AssertError(row, 1, "already used"),
            row => AssertError(row, 2, "API key"),
            row => AssertError(row, 3, "base URL"),
            row => AssertError(row, 4, "base URL"),
            row => AssertError(row, 5, "API key"));
        Assert.Empty(responses.Requests);
        Assert.DoesNotContain("private-test-key", result.GetRawText());
    }

    [Fact]
    public async Task A_status_response_over_the_size_limit_is_rejected_without_returning_response_data()
    {
        var responses = new RecordedHttpResponses();
        responses.RecordBody(First + Status, """{"appName":"Radarr","version":"4.0.5"}""" + new string(' ', 65_536) + FirstKey);
        using var http = new HttpClient(responses);

        var result = await TestAsync(http, [new() { BaseUrl = First, ApiKey = FirstKey }]);

        AssertError(Assert.Single(result.GetProperty("Results").EnumerateArray()), 0, "API request failed");
        Assert.DoesNotContain(FirstKey, result.GetRawText());
    }

    [Fact]
    public async Task A_status_timeout_is_returned_as_a_row_error()
    {
        var responses = new RecordedHttpResponses();
        responses.RecordPending(First + Status, new TaskCompletionSource<HttpResponseMessage>().Task);
        using var http = new HttpClient(responses) { Timeout = TimeSpan.FromMilliseconds(50) };

        var result = await TestAsync(http, [new() { BaseUrl = First, ApiKey = FirstKey }]);

        AssertError(Assert.Single(result.GetProperty("Results").EnumerateArray()), 0, "timed out");
    }

    [Fact]
    public async Task Cancelling_the_request_stops_testing_without_sending_to_later_servers()
    {
        using var cancellation = new CancellationTokenSource();
        var responses = new RecordedHttpResponses(_ => cancellation.Cancel());
        responses.RecordPending(First + Status, new TaskCompletionSource<HttpResponseMessage>().Task);
        using var http = new HttpClient(responses);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => TestAsync(http, TwoServers(), cancellation.Token));

        Assert.Equal([First + Status], responses.Requests);
    }

    private static ArrServerConfiguration[] TwoServers() =>
        [new() { BaseUrl = First, ApiKey = FirstKey }, new() { BaseUrl = Second, ApiKey = SecondKey }];

    private static async Task<JsonElement> TestAsync(HttpClient http, ArrServerConfiguration?[] servers,
        CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new { ArrServers = servers });
        var request = JsonSerializer.Deserialize<ArrServerTestRequest>(body, JsonDefaults.Options)!;
        var controller = new ArrServerTestController(new ArrServerTester(http));
        var result = Assert.IsType<OkObjectResult>(await controller.TestAsync(request, cancellationToken));
        return JsonSerializer.SerializeToElement(result.Value, JsonDefaults.PascalCaseOptions);
    }

    private static void AssertError(JsonElement row, int index, string problem)
    {
        Assert.Equal(index, row.GetProperty("Index").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("Service").ValueKind);
        Assert.Contains(problem, row.GetProperty("Message").GetString());
    }
}
