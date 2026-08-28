namespace GelarkBot.Tests;

public class MobileTypesTests
{
    [Fact]
    public void Expand_SingleVersion()
    {
        Assert.Equal(["Android 12"], MobileTypes.Expand("Android 12"));
    }

    [Fact]
    public void Expand_Range()
    {
        Assert.Equal(
            ["Android 12", "Android 13", "Android 14", "Android 15", "Android 16"],
            MobileTypes.Expand("Android 12-16"));
    }

    [Fact]
    public void Expand_RangeWithoutPrefix()
    {
        Assert.Equal(["Android 13", "Android 14"], MobileTypes.Expand("13-14"));
    }

    [Fact]
    public void Expand_CommaListDeduplicates()
    {
        Assert.Equal(
            ["Android 12", "Android 14", "Android 15"],
            MobileTypes.Expand("Android 12, 14-15, android 14"));
    }

    [Fact]
    public void Expand_PassesUnknownValuesThrough()
    {
        Assert.Equal(["Android 14 Beta"], MobileTypes.Expand("Android 14 Beta"));
    }

    [Fact]
    public void Expand_ReversedRangeThrows()
    {
        var ex = Assert.Throws<FormatException>(() => MobileTypes.Expand("Android 16-12"));
        Assert.Contains("16-12", ex.Message);
    }

    [Fact]
    public void Expand_EmptyThrows()
    {
        Assert.Throws<FormatException>(() => MobileTypes.Expand("  "));
    }

    [Fact]
    public void Pick_ReturnsValueFromRange()
    {
        var picked = MobileTypes.Pick("Android 12-16", new Random(42));
        Assert.Contains(picked, MobileTypes.Expand("Android 12-16"));
    }
}
