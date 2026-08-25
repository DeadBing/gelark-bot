namespace GelarkBot;

internal static class HttpUrl
{
    public static Uri Combine(string baseUrl, string path)
    {
        return new Uri($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
    }
}
