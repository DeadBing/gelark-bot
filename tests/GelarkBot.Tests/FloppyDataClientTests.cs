using System.Net;
using System.Text.Json;

namespace GelarkBot.Tests;

public class FloppyDataClientTests
{
    [Fact]
    public async Task ListStaticProxies_FiltersByCountryAndSendsApiKey()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""
                {
                  "items": [
                    {
                      "id": 1,
                      "ip": "1.1.1.1",
                      "proxyType": "isp",
                      "countryCode": "US",
                      "countryName": "United States",
                      "connection": {
                        "host": "1.1.1.1",
                        "port": 1080,
                        "username": "u",
                        "password": "p",
                        "connectionString": "http://u:p@1.1.1.1:1080"
                      }
                    },
                    {
                      "id": 2,
                      "ip": "2.2.2.2",
                      "proxyType": "isp",
                      "countryCode": "DE",
                      "countryName": "Germany",
                      "connection": {
                        "host": "2.2.2.2",
                        "port": 1080,
                        "username": "u",
                        "password": "p",
                        "connectionString": "http://u:p@2.2.2.2:1080"
                      }
                    }
                  ],
                  "pendingCount": 0
                }
                """),
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings());

        var proxies = await client.ListStaticProxiesAsync("US");

        Assert.Single(proxies);
        Assert.Equal("http://u:p@1.1.1.1:1080", proxies[0].ConnectionString);
        Assert.Equal("floppy-key", handler.Requests[0].Headers["X-Api-Key"]);
        Assert.EndsWith("/v2/proxy/static", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Allocate_Rotating_CreatesUniqueSessions()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, body) =>
            {
                using var doc = JsonDocument.Parse(body);
                var session = doc.RootElement.GetProperty("session").GetString();
                return TestHttp.Json($$"""
                    {
                      "connection": {
                        "protocol": "socks5",
                        "host": "geo.g-w.info",
                        "port": 10800,
                        "username": "user-{{session}}",
                        "password": "pw",
                        "connectionString": "socks5://user-{{session}}:pw@geo.g-w.info:10800"
                      }
                    }
                    """);
            },
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings(proxyMode: "rotating"));

        var proxies = await client.AllocateAsync(2, "qa");

        Assert.Equal(2, proxies.Count);
        Assert.Equal("qa-001", proxies[0].Session);
        Assert.Equal("qa-002", proxies[1].Session);
        Assert.Contains("qa-001", proxies[0].ConnectionString);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("\"rotation\":0", handler.Requests[0].Body);
        Assert.Contains("\"type\":\"residential\"", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Allocate_Static_FailsWhenInventoryIsShort()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""{"items":[],"pendingCount":0}"""),
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings());

        var ex = await Assert.ThrowsAsync<FloppyDataException>(() => client.AllocateAsync(1));
        Assert.Contains("found 0", ex.Message);
    }

    [Fact]
    public async Task Error_UsesFloppyMessage()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json(
                """{"error":{"code":"unauthorized","message":"Invalid API key"}}""",
                HttpStatusCode.Unauthorized),
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings());

        var ex = await Assert.ThrowsAsync<FloppyDataException>(() => client.ListStaticProxiesAsync());
        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("Invalid API key", ex.Message);
    }
}
