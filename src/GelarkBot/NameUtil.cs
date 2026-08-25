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
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            return connectionString;
        }

        if (string.IsNullOrEmpty(uri.UserInfo) || !uri.UserInfo.Contains(':'))
        {
            return connectionString;
        }

        var user = uri.UserInfo.Split(':')[0];
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{user}:***@{uri.Host}{port}";
    }
}
