namespace GelarkBot.Tests;

public class ProxyUrlTests
{
    [Fact]
    public void Parse_ReadsUserInfoUrl()
    {
        var parsed = ProxyUrl.Parse(new ProxyEndpoint
        {
            ConnectionString = "http://user-abc:secret@geo.g-w.info:10080",
            Source = "rotating",
        });

        Assert.Equal("http", parsed.Protocol);
        Assert.Equal("geo.g-w.info", parsed.Host);
        Assert.Equal(10080, parsed.Port);
        Assert.Equal("user-abc", parsed.Username);
        Assert.Equal("secret", parsed.Password);
    }

    [Fact]
    public void ForGeeLark_UsesColonFormat()
    {
        var formatted = ProxyUrl.ForGeeLark(new ProxyEndpoint
        {
            ConnectionString = "http://user-abc:secret@geo.g-w.info:10080",
            Source = "rotating",
        });

        Assert.Equal("http://geo.g-w.info:10080:user-abc:secret", formatted);
    }

    [Fact]
    public void Parse_ReadsColonFormat()
    {
        var parsed = ProxyUrl.ParseString("socks5://1.1.1.1:10800:user:pass")!;

        Assert.Equal("socks5", parsed.Protocol);
        Assert.Equal("1.1.1.1", parsed.Host);
        Assert.Equal(10800, parsed.Port);
        Assert.Equal("user", parsed.Username);
        Assert.Equal("pass", parsed.Password);
    }

    [Fact]
    public void WithServer_KeepsOriginalConnectionString()
    {
        var original = new ProxyEndpoint
        {
            ConnectionString = "http://u:p@geo.g-w.info:10080",
            Source = "rotating",
            Host = "geo.g-w.info",
            Port = 10080,
            Username = "u",
            Password = "p",
            Protocol = "http",
        };

        var updated = ProxyUrl.WithServer(original, "9.9.9.9");

        Assert.Equal("9.9.9.9", updated.Host);
        Assert.Equal("http://u:p@geo.g-w.info:10080", updated.ConnectionString);
        Assert.Equal("http://9.9.9.9:10080:u:p", ProxyUrl.ForGeeLark(updated));
    }
}
