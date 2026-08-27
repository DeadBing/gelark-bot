namespace GelarkBot.Tests;

public class AppSettingsTests
{
    [Fact]
    public void DescribeProxy_RotatingStickyMobile()
    {
        var settings = new AppSettings
        {
            ProxyMode = "rotating",
            ProxyType = "mobile",
            ProxyCountry = "us",
            ProxyProtocol = "socks5",
            ProxyRotation = 0,
        };

        Assert.Equal("FloppyData mobile US sticky session (rotation=0, IP holds per profile), GB balance, socks5", settings.DescribeProxy());
    }

    [Fact]
    public void DescribeProxy_StaticInventory()
    {
        var settings = new AppSettings { ProxyMode = "static", ProxyCountry = "US" };
        Assert.Equal("FloppyData static inventory (US)", settings.DescribeProxy());
    }
}
