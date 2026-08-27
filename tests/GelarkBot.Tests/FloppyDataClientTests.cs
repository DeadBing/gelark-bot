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
        Assert.Contains("no assigned static IPs", ex.Message);
        Assert.Contains("--proxy-mode rotating", ex.Message);
    }

    [Fact]
    public async Task Allocate_Static_ExplainsOtherCountries()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""
                {
                  "items": [
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
        var client = new FloppyDataClient(http, TestHttp.Settings(country: "US"));

        var ex = await Assert.ThrowsAsync<FloppyDataException>(() => client.AllocateAsync(3));
        Assert.Contains("in US, found 0", ex.Message);
        Assert.Contains("DE:1", ex.Message);
        Assert.Contains("--country", ex.Message);
    }

    [Fact]
    public async Task ListStatic_BuildsConnectionStringFromParts()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""
                {
                  "items": [
                    {
                      "id": 9,
                      "ip": "9.9.9.9",
                      "country": "gb",
                      "connection": {
                        "protocol": "socks5",
                        "host": "9.9.9.9",
                        "port": 1080,
                        "username": "u",
                        "password": "p"
                      }
                    }
                  ],
                  "pendingCount": 1
                }
                """),
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings());

        var inventory = await client.ListStaticInventoryAsync();
        Assert.Equal(1, inventory.PendingCount);
        Assert.Equal("GB", inventory.Items[0].Country);
        Assert.Equal("socks5://u:p@9.9.9.9:1080", inventory.Items[0].ConnectionString);
    }

    [Fact]
    public async Task Check_UsesStructuredFieldsWhenPresent()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, body) =>
            {
                Assert.Contains("connectionString", body);
                Assert.Contains("geo.g-w.info", body);
                return TestHttp.Json("""
                    {
                      "ip": "8.8.8.8",
                      "location": { "countryCode": "US", "country": "United States" }
                    }
                    """);
            },
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings());

        var check = await client.CheckAsync(new ProxyEndpoint
        {
            ConnectionString = "http://u:p@geo.g-w.info:10080",
            Source = "rotating",
            Protocol = "http",
            Host = "geo.g-w.info",
            Port = 10080,
            Username = "u",
            Password = "p",
        });

        Assert.True(check.Ok);
        Assert.Equal("8.8.8.8", check.Ip);
        Assert.Equal("US", check.Country);
        Assert.EndsWith("/v2/proxy/check", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Check_FallsBackToStructuredFieldsWhenConnectionStringFails()
    {
        var calls = 0;
        var handler = new ScriptedHandler
        {
            Responder = (_, body) =>
            {
                calls++;
                if (body.Contains("connectionString"))
                {
                    return TestHttp.Json(
                        """{"error":{"code":"invalid_request","message":"Failed to check proxy. Verify the proxy configuration."}}""",
                        HttpStatusCode.BadRequest);
                }

                Assert.Contains("\"host\":\"geo.g-w.info\"", body);
                return TestHttp.Json("""{ "ip": "9.9.9.9", "location": { "countryCode": "US" } }""");
            },
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings());

        var check = await client.CheckAsync(new ProxyEndpoint
        {
            ConnectionString = "http://u:p@geo.g-w.info:10080",
            Source = "rotating",
            Protocol = "http",
            Host = "geo.g-w.info",
            Port = 10080,
            Username = "u",
            Password = "p",
        });

        Assert.True(check.Ok);
        Assert.Equal("9.9.9.9", check.Ip);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetRotatingBalance_ReadsTotalGb()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""
                {
                  "expiring": { "expiresAt": "2026-09-01T00:00:00Z", "traffic": { "availableBytes": 0, "availableGb": 0 } },
                  "nonExpiring": { "traffic": { "availableBytes": 5368709120, "availableGb": 5 } },
                  "total": { "traffic": { "availableBytes": 5368709120, "availableGb": 5 } }
                }
                """),
        };
        using var http = new HttpClient(handler);
        var client = new FloppyDataClient(http, TestHttp.Settings());

        var balance = await client.GetRotatingBalanceAsync();
        Assert.Equal(5, balance.TotalGb);
        Assert.False(balance.IsEmpty);
        Assert.EndsWith("/v2/proxy/rotating/balance", handler.Requests[0].Uri.AbsolutePath);
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
