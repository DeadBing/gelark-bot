namespace GelarkBot.Tests;

public class EmailPoolTests
{
    [Theory]
    [InlineData("alice@example.com", "alice@example.com", null, null)]
    [InlineData("alice@example.com:secret", "alice@example.com", "secret", null)]
    [InlineData("alice@example.com:secret:JBSWY3DPEHPK3PXP", "alice@example.com", "secret", "JBSWY3DPEHPK3PXP")]
    [InlineData("user123:p:w:JBSWY3DPEHPK3PXP", "user123", "p:w", "JBSWY3DPEHPK3PXP")]
    public void ParseLine_UsesLoginPasswordTotp(string line, string login, string? password, string? totp)
    {
        var parsed = EmailPool.ParseLine(line);
        Assert.NotNull(parsed);
        Assert.Equal(login, parsed!.Login);
        Assert.Equal(password, parsed.Password);
        Assert.Equal(totp, parsed.TotpSecret);
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
    public void ParseLine_RejectsEmptyLoginWithoutEchoingSecrets()
    {
        var ex = Assert.Throws<FormatException>(() => EmailPool.ParseLine(":password:totp"));
        Assert.Equal("Invalid account line: login is empty", ex.Message);
        Assert.DoesNotContain("password", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("totp", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_ReadsFileAndRejectsDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emails-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "a@x.com:one:TOTP1\n# skip\nb@x.com:two:TOTP2\n");
        try
        {
            var emails = EmailPool.Load(path);
            Assert.Equal(2, emails.Count);
            Assert.Equal("a@x.com", emails[0].Login);
            Assert.Equal("one", emails[0].Password);
            Assert.Equal("TOTP1", emails[0].TotpSecret);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Take_RejectsShortPool()
    {
        var pool = new[] { new EmailCredential { Login = "a@x.com" } };
        var ex = Assert.Throws<InvalidOperationException>(() => EmailPool.Take(pool, 2));
        Assert.Contains("Need 2 accounts", ex.Message);
    }
}
