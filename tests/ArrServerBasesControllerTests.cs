using System.Text.Json;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.MagicCollections.Configuration;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MagicCollections.Tests;

public sealed class ArrServerBasesControllerTests
{
    [Theory]
    [InlineData("""{"ArrBaseUrls":[]}""")]
    [InlineData("""{"ArrBaseUrls":["http://unavailable.example.invalid:7878/proxy"],"Service":"Unknown"}""")]
    public void Validating_base_URLs_requires_neither_keys_nor_a_server_connection(string body)
    {
        var response = Validate(body);

        Assert.Equal(JsonValueKind.Null, response.GetProperty("Error").ValueKind);
    }

    [Theory]
    [InlineData("https://MEDIA.example.invalid:443/Proxy/", "already used")]
    [InlineData("https://media.example.invalid/other?apikey=secret-key", "valid HTTP or HTTPS")]
    public void An_invalid_base_returns_only_a_safe_message_and_zero_based_row_index(string secondBase, string message)
    {
        var response = Validate($$"""{"ArrBaseUrls":["https://media.example.invalid/Proxy","{{secondBase}}"]}""");

        var error = response.GetProperty("Error");
        Assert.Equal(1, error.GetProperty("Index").GetInt32());
        Assert.Contains(message, error.GetProperty("Message").GetString());
        Assert.Equal(2, error.EnumerateObject().Count());
        Assert.DoesNotContain("secret-key", response.GetRawText());
    }

    private static JsonElement Validate(string body)
    {
        var request = JsonSerializer.Deserialize<ArrServerBasesRequest>(body, JsonDefaults.Options)!;
        var result = Assert.IsType<OkObjectResult>(new ArrServerBasesController().Validate(request));
        return JsonSerializer.SerializeToElement(result.Value, JsonDefaults.PascalCaseOptions);
    }
}
