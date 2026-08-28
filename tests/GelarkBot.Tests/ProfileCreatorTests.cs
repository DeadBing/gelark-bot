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
                Emails = [new EmailCredential { Login = "alice@example.com", Password = "pw", TotpSecret = "JBSWY3DPEHPK3PXP" }],
                DryRun = true,
            });

            Assert.True(result.DryRun);
            Assert.Equal(1, result.Success);
            Assert.Equal("alice", result.Profiles[0].ProfileName);
            Assert.Equal("alice@example.com", result.Profiles[0].Login);
            Assert.Equal("pw", result.Profiles[0].Password);
            Assert.Equal("JBSWY3DPEHPK3PXP", result.Profiles[0].TotpSecret);
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

    [Fact]
    public async Task Create_RetriesGeeLarkCheckOnResolvedIpAndSendsSerial()
    {
        var output = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.json");
        var settings = TestHttp.Settings(outputFile: output);
        var floppyHandler = new ScriptedHandler
        {
            Responder = (request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/v2/proxy/static"))
                {
                    return TestHttp.Json("""
                        {
                          "items": [
                            {
                              "id": 1,
                              "ip": "1.1.1.1",
                              "countryCode": "US",
                              "connection": {
                                "protocol": "http",
                                "host": "proxy.example",
                                "port": 10080,
                                "username": "u",
                                "password": "p",
                                "connectionString": "http://u:p@proxy.example:10080"
                              }
                            }
                          ],
                          "pendingCount": 0
                        }
                        """);
                }

                return TestHttp.Json("""{ "ip": "8.8.8.8", "location": { "countryCode": "US" } }""");
            },
        };
        var geeLarkHandler = new ScriptedHandler
        {
            Responder = (request, body) =>
            {
                var path = request.RequestUri!.AbsolutePath;
                if (path.EndsWith("/open/v1/proxy/check"))
                {
                    if (body.Contains("\"server\":\"9.9.9.9\""))
                    {
                        return TestHttp.Json("""
                            {
                              "code": 0,
                              "data": { "detectStatus": true, "outboundIP": "8.8.8.8", "countryName": "United States" }
                            }
                            """);
                    }

                    return TestHttp.Json("""
                        {
                          "code": 0,
                          "data": { "detectStatus": false, "message": "timeout" }
                        }
                        """);
                }

                if (path.EndsWith("/open/v1/proxy/add"))
                {
                    Assert.Contains("\"server\":\"9.9.9.9\"", body);
                    return TestHttp.Json("""
                        {
                          "code": 0,
                          "data": { "successDetails": [ { "id": "proxy-1" } ] }
                        }
                        """);
                }

                if (path.EndsWith("/open/v1/proxy/list"))
                {
                    return TestHttp.Json("""
                        {
                          "code": 0,
                          "data": { "list": [ { "id": "proxy-1", "serialNo": 7, "server": "9.9.9.9", "port": 10080, "username": "u" } ] }
                        }
                        """);
                }

                Assert.Contains("\"proxyNumber\":7", body);
                return TestHttp.Json("""
                    {
                      "code": 0,
                      "msg": "success",
                      "data": {
                        "details": [
                          { "index": 1, "code": 0, "msg": "success", "id": "phone-1", "profileName": "gl-001" }
                        ]
                      }
                    }
                    """);
            },
        };

        using var floppyHttp = new HttpClient(floppyHandler);
        using var geeLarkHttp = new HttpClient(geeLarkHandler);
        var creator = new ProfileCreator(
            new FloppyDataClient(floppyHttp, settings),
            new GeeLarkClient(geeLarkHttp, settings),
            settings,
            resolveIpv4: (_, _) => Task.FromResult<string?>("9.9.9.9"),
            liveProbe: (_, _) => Task.FromResult(new ProxyCheckResult { Source = "local", Ok = true, Ip = "8.8.8.8" }));

        try
        {
            var result = await creator.CreateAsync(new CreateRequest { Count = 1 });

            Assert.True(result.Profiles[0].Ok);
            Assert.Equal("phone-1", result.Profiles[0].Id);
            Assert.Contains(result.Profiles[0].Diagnostics, line => line.Contains("9.9.9.9"));
            Assert.Contains(geeLarkHandler.Requests, item => item.Uri.AbsolutePath.EndsWith("/open/v1/proxy/check"));
            Assert.Contains(geeLarkHandler.Requests, item => item.Uri.AbsolutePath.EndsWith("/open/v1/phone/addNew"));
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
    public async Task CheckOnly_DoesNotCreatePhones()
    {
        var output = Path.Combine(Path.GetTempPath(), $"profiles-{Guid.NewGuid():N}.json");
        var settings = TestHttp.Settings(outputFile: output);
        var floppyHandler = new ScriptedHandler
        {
            Responder = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/v2/proxy/static")
                ? TestHttp.Json("""
                    {
                      "items": [
                        {
                          "id": 1,
                          "ip": "1.1.1.1",
                          "countryCode": "US",
                          "connection": {
                            "host": "1.1.1.1",
                            "port": 80,
                            "username": "u",
                            "password": "p",
                            "connectionString": "http://u:p@1.1.1.1:80"
                          }
                        }
                      ],
                      "pendingCount": 0
                    }
                    """)
                : TestHttp.Json("""{ "ip": "1.1.1.1", "location": { "countryCode": "US" } }"""),
        };
        var geeLarkHandler = new ScriptedHandler
        {
            Responder = (request, _) =>
            {
                Assert.DoesNotContain("addNew", request.RequestUri!.AbsolutePath);
                Assert.DoesNotContain("/proxy/add", request.RequestUri.AbsolutePath);
                return TestHttp.Json("""
                    {
                      "code": 0,
                      "data": { "detectStatus": true, "outboundIP": "1.1.1.1" }
                    }
                    """);
            },
        };
        using var floppyHttp = new HttpClient(floppyHandler);
        using var geeLarkHttp = new HttpClient(geeLarkHandler);
        var creator = new ProfileCreator(
            new FloppyDataClient(floppyHttp, settings),
            new GeeLarkClient(geeLarkHttp, settings),
            settings,
            liveProbe: (_, _) => Task.FromResult(new ProxyCheckResult { Source = "local", Ok = true, Ip = "1.1.1.1" }));

        try
        {
            var result = await creator.CreateAsync(new CreateRequest { Count = 1, CheckOnly = true });
            Assert.True(result.Profiles[0].Ok);
            Assert.Null(result.Profiles[0].Id);
            Assert.DoesNotContain(geeLarkHandler.Requests, item => item.Uri.AbsolutePath.EndsWith("/open/v1/phone/addNew"));
        }
        finally
        {
            var checkFile = Path.Combine(Path.GetDirectoryName(output)!, "last-proxy-check.json");
            if (File.Exists(output))
            {
                File.Delete(output);
            }

            if (File.Exists(checkFile))
            {
                File.Delete(checkFile);
            }
        }
    }
}
