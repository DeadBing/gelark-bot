namespace GelarkBot.Tests;

public class ProfileCreatorTests
{
    [Fact]
    public async Task DryRun_WritesMappingAndDoesNotCallGeeLark()
    {
        var floppyHandler = new ScriptedHandler
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
                    }
                  ],
                  "pendingCount": 0
                }
                """),
        };
        var geeLarkHandler = new ScriptedHandler
        {
            Responder = (_, _) => throw new InvalidOperationException("GeeLark should not be called in dry-run"),
        };
        var output = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.json");
        var settings = TestHttp.Settings(outputFile: output);
        using var floppyHttp = new HttpClient(floppyHandler);
        using var geeLarkHttp = new HttpClient(geeLarkHandler);
        var creator = new ProfileCreator(
            new FloppyDataClient(floppyHttp, settings),
            new GeeLarkClient(geeLarkHttp, settings),
            settings);

        try
        {
            var result = await creator.CreateAsync(new CreateRequest
            {
                Count = 1,
                Emails = [new EmailCredential { Email = "alice@example.com", Password = "pw" }],
                DryRun = true,
            });

            Assert.True(result.DryRun);
            Assert.Equal(1, result.Success);
            Assert.Equal("alice", result.Profiles[0].ProfileName);
            Assert.Equal("alice@example.com", result.Profiles[0].Email);
            Assert.Equal("pw", result.Profiles[0].EmailPassword);
            Assert.Empty(geeLarkHandler.Requests);
            Assert.True(File.Exists(output));
            Assert.Contains("alice@example.com", File.ReadAllText(output));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Fact]
    public async Task Create_RequiresGeeLarkClient()
    {
        var floppyHandler = new ScriptedHandler
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
                    }
                  ],
                  "pendingCount": 0
                }
                """),
        };
        using var floppyHttp = new HttpClient(floppyHandler);
        var settings = TestHttp.Settings(outputFile: Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.json"));
        var creator = new ProfileCreator(new FloppyDataClient(floppyHttp, settings), null, settings);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => creator.CreateAsync(new CreateRequest { Count = 1 }));
        Assert.Contains("GeeLark client is required", ex.Message);
    }
}
