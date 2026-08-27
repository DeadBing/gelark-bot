using System.Text.RegularExpressions;

namespace GelarkBot;

public static class NameUtil
{
    private static readonly Regex Unsafe = new(@"[^a-zA-Z0-9._-]+", RegexOptions.Compiled);

    public static string Sanitize(string value, int maxLength = 40)
    {
        var cleaned = Unsafe.Replace(value, "-").Trim('-', '.', '_');
        if (cleaned.Length == 0)
        {
            cleaned = "profile";
        }

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    public static string RedactProxy(string connectionString)
    {
        var parsed = ProxyUrl.ParseString(connectionString);
        if (parsed is null || string.IsNullOrEmpty(parsed.Password) || string.IsNullOrWhiteSpace(parsed.Host))
        {
            return connectionString;
        }

        var scheme = string.IsNullOrWhiteSpace(parsed.Protocol) ? "http" : parsed.Protocol;
        var port = parsed.Port is > 0 ? $":{parsed.Port}" : "";
        if (connectionString.Contains('@'))
        {
            return $"{scheme}://{parsed.Username}:***@{parsed.Host}{port}";
        }

        return $"{scheme}://{parsed.Host}{port}:{parsed.Username}:***";
    }
}
