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
    public void Build_AssignsMobileTypeFromPool()
    {
        var proxies = Enumerable.Range(1, 10)
            .Select(i => new ProxyEndpoint { ConnectionString = $"http://u:p@1.1.1.{i}:80", Source = "rotating" })
            .ToArray();
        var mobileTypes = MobileTypes.Expand("Android 12-16");

        var plans = ProfilePlanner.Build(proxies, null, "gl", null, true, mobileTypes, new Random(7));

        Assert.All(plans, plan => Assert.Contains(plan.MobileType, mobileTypes));
        // With ten draws from five versions the seed must produce some variety.
        Assert.True(plans.Select(plan => plan.MobileType).Distinct().Count() > 1);
    }

    [Fact]
    public void Build_LeavesMobileTypeNullWithoutPool()
    {
        var proxies = new[]
        {
            new ProxyEndpoint { ConnectionString = "http://u:p@1.1.1.1:80", Source = "rotating" },
        };

        var plans = ProfilePlanner.Build(proxies, null, "gl", null, true);

        Assert.Null(plans[0].MobileType);
    }

    [Fact]
    public void RedactProxy_HidesPassword()
    {
        Assert.Equal("socks5://user:***@host.example:1080", NameUtil.RedactProxy("socks5://user:secret@host.example:1080"));
        Assert.Equal("http://geo.g-w.info:10080:user:***", NameUtil.RedactProxy("http://geo.g-w.info:10080:user:secret"));
    }
}
