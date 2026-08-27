using System.Text.Json;

namespace GelarkBot.Tests;

public class GeeLarkCreateParserTests
{
    [Fact]
    public void Map_UsesOrderWhenGeeLarkRenamesProfileAndIndexIsZero()
    {
        var plan = Plan("gl-001");
        var details = new[]
        {
            new CreatePhoneDetail
            {
                Index = 0,
                Code = 0,
                Msg = "success",
                Id = "497652752864775437",
                ProfileName = "22 ungrouped",
                EnvSerialNo = "22",
            },
        };

        var mapped = GeeLarkCreateParser.Map([plan], 0, "success", details);

        Assert.True(mapped[0].Ok);
        Assert.Equal("497652752864775437", mapped[0].Id);
        Assert.Equal("22", mapped[0].EnvSerialNo);
    }

    [Fact]
    public void Map_DoesNotTreatTopLevelSuccessAsError()
    {
        var mapped = GeeLarkCreateParser.Map([Plan("gl-001")], 0, "success", []);

        Assert.False(mapped[0].Ok);
        Assert.NotEqual("success", mapped[0].Error);
        Assert.Contains("phones", mapped[0].Error);
    }

    [Fact]
    public void Map_ExplainsGeeLarkProxyCheckFailure()
    {
        var details = new[]
        {
            new CreatePhoneDetail { Index = 1, Code = 45004, Msg = "check proxy failed" },
        };

        var mapped = GeeLarkCreateParser.Map([Plan("gl-001")], 40006, "partial success", details);

        Assert.False(mapped[0].Ok);
        Assert.Contains("check proxy failed", mapped[0].Error);
        Assert.DoesNotContain("--protocol http", mapped[0].Error);
        Assert.Contains("geo.g-w.info", mapped[0].Error);
    }

    [Fact]
    public void ReadDetails_AcceptsItemsAliasAndEnvId()
    {
        using var doc = JsonDocument.Parse("""
            {
              "items": [
                { "index": 1, "code": 0, "msg": "success", "envId": "env-9", "serialName": "gl-001" }
              ]
            }
            """);

        var details = GeeLarkCreateParser.ReadDetails(doc.RootElement);
        var mapped = GeeLarkCreateParser.Map([Plan("gl-001")], 0, "success", details);

        Assert.True(mapped[0].Ok);
        Assert.Equal("env-9", mapped[0].Id);
    }

    [Fact]
    public async Task CreatePhones_AcceptsRenamedProfileWithZeroIndex()
    {
        var handler = new ScriptedHandler
        {
            Responder = (_, _) => TestHttp.Json("""
                {
                  "code": 0,
                  "msg": "success",
                  "data": {
                    "successAmount": 1,
                    "details": [
                      { "index": 0, "code": 0, "msg": "success", "id": "phone-9", "profileName": "9 ungrouped" }
                    ]
                  }
                }
                """),
        };
        using var http = new HttpClient(handler);
        var client = new GeeLarkClient(http, TestHttp.Settings());

        var created = await client.CreatePhonesAsync([Plan("gl-001")]);

        Assert.True(created[0].Ok);
        Assert.Equal("phone-9", created[0].Id);
    }

    private static ProfilePlan Plan(string name) => new()
    {
        ProfileName = name,
        Proxy = new ProxyEndpoint { ConnectionString = "http://u:p@1.1.1.1:80", Source = "rotating" },
    };
}
