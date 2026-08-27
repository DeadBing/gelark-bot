using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GelarkBot.Tests;

public class GeeLarkClientTests
{
    [Fact]
    public async Task CreatePhones_SendsBearerTokenAndProxyInformation()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""
                {
                  "traceId": "ABC",
                  "code": 0,
                  "msg": "success",
                  "data": {
                    "totalAmount": 1,
                    "successAmount": 1,
                    "failAmount": 0,
                    "details": [
                      {
                        "index": 1,
                        "code": 0,
                        "msg": "success",
                        "id": "phone-1",
                        "profileName": "alice",
                        "envSerialNo": "22",
                        "equipmentInfo": { "countryName": "United States" }
                      }
                    ]
                  }
                }
                """),
        };
        using var http = new HttpClient(handler);
        var client = new GeeLarkClient(http, TestHttp.Settings());
        var plan = new ProfilePlan
        {
            ProfileName = "alice",
            Proxy = new ProxyEndpoint { ConnectionString = "socks5://u:p@1.1.1.1:1080", Source = "static" },
            Email = new EmailCredential { Login = "alice@example.com" },
            ProfileNote = "login: alice@example.com",
            ProfileTags = ["floppydata"],
        };

        var created = await client.CreatePhonesAsync([plan]);

        Assert.True(created[0].Ok);
        Assert.Equal("phone-1", created[0].Id);
        Assert.Equal("Bearer token", handler.Requests[0].Headers["Authorization"]);
        Assert.True(handler.Requests[0].Headers.ContainsKey("traceId"));
        Assert.Equal(32, handler.Requests[0].Headers["traceId"].Length);
        Assert.Contains("socks5://u:p@1.1.1.1:1080", handler.Requests[0].Body);
        Assert.Contains("\"profileNote\":\"login: alice@example.com\"", handler.Requests[0].Body);
        Assert.Contains("\"mobileType\":\"Android 12\"", handler.Requests[0].Body);
        Assert.Contains("\"proxyQueryChannel\":1", handler.Requests[0].Body);
        Assert.EndsWith("/open/v1/phone/addNew", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task CreatePhones_FallsBackToSingleCreateOnBatchLimit()
    {
        var calls = 0;
        var handler = new ScriptedHandler
        {
            Responder = (_, body) =>
            {
                calls++;
                using var doc = JsonDocument.Parse(body);
                var count = doc.RootElement.GetProperty("data").GetArrayLength();
                if (count > 1)
                {
                    return TestHttp.Json("""{"code":44001,"msg":"Batch creation is not allowed"}""");
                }

                var name = doc.RootElement.GetProperty("data")[0].GetProperty("profileName").GetString();
                return TestHttp.Json($$"""
                    {
                      "code": 0,
                      "msg": "success",
                      "data": {
                        "details": [
                          { "index": 1, "code": 0, "msg": "success", "id": "{{name}}-id", "profileName": "{{name}}" }
                        ]
                      }
                    }
                    """);
            },
        };
        using var http = new HttpClient(handler);
        var settings = new AppSettings
        {
            GeeLarkToken = "token",
            GeeLarkBaseUrl = "https://openapi.geelark.com",
            FloppyDataApiKey = "floppy-key",
            FloppyDataBaseUrl = "https://api.floppydata.net",
            BatchSize = 2,
            MobileType = "Android 12",
            Region = "sgp",
        };
        var client = new GeeLarkClient(http, settings);
        var plans = new[]
        {
            Plan("one"),
            Plan("two"),
        };

        var created = await client.CreatePhonesAsync(plans, batchSize: 2);

        Assert.Equal(3, calls);
        Assert.Equal(2, created.Count);
        Assert.All(created, item => Assert.True(item.Ok));
        Assert.Equal("one-id", created[0].Id);
        Assert.Equal("two-id", created[1].Id);
    }

    [Fact]
    public async Task CreatePhones_RecordsPerItemFailure()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""
                {
                  "code": 40006,
                  "msg": "partial success",
                  "data": {
                    "details": [
                      { "index": 1, "code": 0, "msg": "success", "id": "ok-id", "profileName": "ok" },
                      { "index": 2, "code": 45004, "msg": "check proxy failed", "profileName": "bad" }
                    ]
                  }
                }
                """),
        };
        using var http = new HttpClient(handler);
        var client = new GeeLarkClient(http, TestHttp.Settings());

        var created = await client.CreatePhonesAsync([Plan("ok"), Plan("bad")], batchSize: 2);

        Assert.True(created[0].Ok);
        Assert.False(created[1].Ok);
        Assert.Equal("check proxy failed", created[1].Error);
    }

    [Fact]
    public async Task KeyAuth_SendsSignHeaders()
    {
        var settings = new AppSettings
        {
            GeeLarkAppId = "app",
            GeeLarkApiKey = "secret",
            GeeLarkBaseUrl = "https://openapi.geelark.com",
        };
        var handler = new ScriptedHandler
        {
            Responder = (request, _) =>
            {
                var traceId = request.Headers.GetValues("traceId").Single();
                var ts = request.Headers.GetValues("ts").Single();
                var nonce = request.Headers.GetValues("nonce").Single();
                var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"app{traceId}{ts}{nonce}secret")));
                Assert.Equal(expected, request.Headers.GetValues("sign").Single());
                Assert.Equal("app", request.Headers.GetValues("appId").Single());
                Assert.Equal(traceId[..6], nonce);
                return TestHttp.Json("""{"code":0,"data":{"total":0,"page":1,"pageSize":20,"items":[]}}""");
            },
        };
        using var http = new HttpClient(handler);
        var client = new GeeLarkClient(http, settings);

        var list = await client.ListPhonesAsync();
        Assert.Equal(0, list.Total);
    }

    [Fact]
    public async Task HttpError_IsWrapped()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""{"code":401,"msg":"unauthorized"}""", HttpStatusCode.Unauthorized),
        };
        using var http = new HttpClient(handler);
        var client = new GeeLarkClient(http, TestHttp.Settings());

        var ex = await Assert.ThrowsAsync<GeeLarkException>(() => client.ListPhonesAsync());
        Assert.Contains("401", ex.Message);
    }

    private static ProfilePlan Plan(string name) => new()
    {
        ProfileName = name,
        Proxy = new ProxyEndpoint { ConnectionString = "http://u:p@1.1.1.1:80", Source = "static" },
    };
}
