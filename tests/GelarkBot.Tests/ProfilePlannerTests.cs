namespace GelarkBot.Tests;

public class ProfilePlannerTests
{
    [Fact]
    public void Build_PairsProxyAndEmail_AndUsesLocalPartAsName()
    {
        var proxies = new[]
        {
            new ProxyEndpoint { ConnectionString = "socks5://u:p@1.1.1.1:1080", Source = "static", Country = "US" },
            new ProxyEndpoint { ConnectionString = "socks5://u:p@2.2.2.2:1080", Source = "static", Country = "US" },
        };
        var emails = new[]
        {
            new EmailCredential { Login = "alice@example.com", Password = "one", TotpSecret = "TOTP" },
            new EmailCredential { Login = "bob@example.com" },
        };

        var plans = ProfilePlanner.Build(proxies, emails, "gl", "qa", true);

        Assert.Equal("alice", plans[0].ProfileName);
        Assert.Equal("bob", plans[1].ProfileName);
        Assert.Equal("login: alice@example.com", plans[0].ProfileNote);
        Assert.Equal("qa", plans[0].ProfileGroup);
        Assert.Contains("floppydata", plans[0].ProfileTags);
        Assert.Contains("US", plans[0].ProfileTags);
        Assert.Equal(proxies[0].ConnectionString, plans[0].Proxy.ConnectionString);
    }

    [Fact]
    public void Build_UsesLoginWhenItIsNotAnEmail()
    {
        var proxies = new[]
        {
            new ProxyEndpoint { ConnectionString = "http://u:p@1.1.1.1:80", Source = "static" },
        };
        var emails = new[]
        {
            new EmailCredential { Login = "user123", Password = "pw", TotpSecret = "TOTP" },
        };

        var plans = ProfilePlanner.Build(proxies, emails, "gl", null, true);

        Assert.Equal("user123", plans[0].ProfileName);
        Assert.Equal("login: user123", plans[0].ProfileNote);
    }

    [Fact]
    public void Build_UsesPrefixWhenEmailsAreMissing()
    {
        var proxies = new[]
        {
            new ProxyEndpoint { ConnectionString = "http://u:p@1.1.1.1:80", Source = "rotating" },
        };

        var plans = ProfilePlanner.Build(proxies, null, "qa", null, true);

        Assert.Equal("qa-001", plans[0].ProfileName);
        Assert.Equal("", plans[0].ProfileNote);
        Assert.Null(plans[0].Email);
    }

    [Fact]
    public void Build_RejectsFewerEmailsThanProxies()
    {
        var proxies = new[]
        {
            new ProxyEndpoint { ConnectionString = "http://u:p@1.1.1.1:80", Source = "static" },
            new ProxyEndpoint { ConnectionString = "http://u:p@2.2.2.2:80", Source = "static" },
        };
        var emails = new[] { new EmailCredential { Login = "a@x.com" } };

        Assert.Throws<InvalidOperationException>(() => ProfilePlanner.Build(proxies, emails, "gl", null, true));
    }

    [Fact]
    public void RedactProxy_HidesPassword()
    {
        Assert.Equal("socks5://user:***@host.example:1080", NameUtil.RedactProxy("socks5://user:secret@host.example:1080"));
    }
}
