using System.Net;
using System.Text.Json;

namespace GelarkBot;

public static class ProxyLiveProbe
{
    public static async Task<ProxyCheckResult> ProbeAsync(
        ProxyEndpoint proxy,
        CancellationToken cancellationToken = default)
    {
        var parsed = ProxyUrl.Parse(proxy);
        var protocol = (parsed.Protocol ?? "http").ToLowerInvariant();
        if (protocol == "socks5")
        {
            return new ProxyCheckResult
            {
                Source = "local",
                Ok = false,
                Message = "SOCKS5 is not probed locally. Retry this session with --protocol http.",
            };
        }

        if (string.IsNullOrWhiteSpace(parsed.Host) || parsed.Port is not > 0)
        {
            return new ProxyCheckResult { Source = "local", Ok = false, Message = "missing host/port" };
        }

        var webProxy = new WebProxy
        {
            Address = new Uri($"http://{parsed.Host}:{parsed.Port}"),
            Credentials = new NetworkCredential(parsed.Username ?? "", parsed.Password ?? ""),
        };
        using var handler = new HttpClientHandler
        {
            Proxy = webProxy,
            UseProxy = true,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };

        try
        {
            using var response = await http.GetAsync("http://ip-api.com/json", cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new ProxyCheckResult
                {
                    Source = "local",
                    Ok = false,
                    Message = $"HTTP {(int)response.StatusCode}",
                };
            }

            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
            var ip = document.RootElement.TryGetProperty("query", out var ipEl) ? ipEl.GetString() : null;
            var country = document.RootElement.TryGetProperty("countryCode", out var ccEl) ? ccEl.GetString() : null;
            return new ProxyCheckResult
            {
                Source = "local",
                Ok = !string.IsNullOrWhiteSpace(ip),
                Ip = ip,
                Country = country,
                Message = string.IsNullOrWhiteSpace(ip) ? "no exit IP" : null,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            return new ProxyCheckResult
            {
                Source = "local",
                Ok = false,
                Message = ex.InnerException?.Message ?? ex.Message,
            };
        }
    }
}
