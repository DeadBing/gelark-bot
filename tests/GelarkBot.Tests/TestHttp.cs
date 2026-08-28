using System.Net;
using System.Text;

namespace GelarkBot.Tests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, IReadOnlyDictionary<string, string> Headers);

internal sealed class ScriptedHandler : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public Func<HttpRequestMessage, string, HttpResponseMessage> Responder { get; set; } =
        (_, _) => new HttpResponseMessage(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, headers));
        return Responder(request, body);
    }
}

internal static class TestHttp
{
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    public static AppSettings Settings(
        string proxyMode = "static",
        string country = "US",
        string outputFile = "data/test-profiles.json")
    {
        return new AppSettings
        {
            GeeLarkToken = "token",
            GeeLarkBaseUrl = "https://openapi.geelark.com",
            FloppyDataApiKey = "floppy-key",
            FloppyDataBaseUrl = "https://api.floppydata.net",
            ProxyMode = proxyMode,
            ProxyCountry = country,
            ProxyType = "residential",
            ProxyProtocol = "socks5",
            ProxyRotation = 0,
            MobileType = "Android 12",
            Region = "sgp",
            OutputFile = outputFile,
            BatchSize = 1,
        };
    }
}
