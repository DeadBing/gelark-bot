namespace GelarkBot.Tests;

public class EmailPoolTests
{
    [Theory]
    [InlineData("alice@example.com", "alice@example.com", null)]
    [InlineData("alice@example.com:secret", "alice@example.com", "secret")]
    [InlineData("alice@example.com,secret", "alice@example.com", "secret")]
    [InlineData("alice@example.com;secret", "alice@example.com", "secret")]
    public void ParseLine_SupportsCommonFormats(string line, string email, string? password)
    {
        var parsed = EmailPool.ParseLine(line);
        Assert.NotNull(parsed);
        Assert.Equal(email, parsed!.Email);
        Assert.Equal(password, parsed.Password);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# comment")]
    [InlineData("   ")]
    public void ParseLine_SkipsEmptyAndComments(string line)
    {
        Assert.Null(EmailPool.ParseLine(line));
    }

    [Fact]
    public void ParseLine_RejectsInvalidEmail()
    {
        Assert.Throws<FormatException>(() => EmailPool.ParseLine("not-an-email"));
    }

    [Fact]
    public void Load_ReadsFileAndRejectsDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "a@x.com:one\n# skip\nb@x.com\n");
        try
        {
            var emails = EmailPool.Load(path);
            Assert.Equal(2, emails.Count);
            Assert.Equal("a@x.com", emails[0].Email);
            Assert.Equal("one", emails[0].Password);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Take_RejectsShortPool()
    {
        var pool = new[] { new EmailCredential { Email = "a@x.com" } };
        var ex = Assert.Throws<InvalidOperationException>(() => EmailPool.Take(pool, 2));
        Assert.Contains("Need 2 emails", ex.Message);
    }
}
