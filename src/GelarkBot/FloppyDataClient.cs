using System.Net.Http.Json;
using System.Text.Json;

namespace GelarkBot;

public sealed class FloppyDataClient
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;

    public FloppyDataClient(HttpClient http, AppSettings settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<IReadOnlyList<ProxyEndpoint>> ListStaticProxiesAsync(
        string? country = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HttpUrl.Combine(_settings.FloppyDataBaseUrl, "/v2/proxy/static"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.FloppyDataApiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<StaticProxyList>(response, cancellationToken);

        var proxies = new List<ProxyEndpoint>();
        foreach (var item in payload.Items)
        {
            var countryCode = string.IsNullOrWhiteSpace(item.CountryCode) ? null : item.CountryCode.ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(country) &&
                !string.Equals(countryCode, country, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var connectionString = item.Connection?.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                continue;
            }

            proxies.Add(new ProxyEndpoint
            {
                ConnectionString = connectionString,
                Source = "static",
                Protocol = ProtocolFromUrl(connectionString) ?? item.Connection?.Protocol,
                Host = item.Connection?.Host,
                Port = item.Connection?.Port,
                Username = item.Connection?.Username,
                Password = item.Connection?.Password,
                Country = countryCode,
                Ip = item.Ip,
                StaticId = item.Id,
            });
        }

        return proxies;
    }

    public async Task<ProxyEndpoint> CreateRotatingConnectionAsync(
        string session,
        CancellationToken cancellationToken = default)
    {
        var body = new RotatingConnectionRequest
        {
            Type = _settings.ProxyType,
            Country = _settings.ProxyCountry.ToUpperInvariant(),
            Protocol = _settings.ProxyProtocol,
            Rotation = _settings.ProxyRotation,
            City = _settings.ProxyCity,
            State = _settings.ProxyState,
            Session = session,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, HttpUrl.Combine(_settings.FloppyDataBaseUrl, "/v2/proxy/rotating/connections"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.FloppyDataApiKey);
        request.Content = JsonContent.Create(body, options: JsonUtil.Options);
        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<RotatingConnectionResponse>(response, cancellationToken);
        var connectionString = payload.Connection?.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new FloppyDataException("FloppyData did not return connection.connectionString");
        }

        return new ProxyEndpoint
        {
            ConnectionString = connectionString,
            Source = "rotating",
            Protocol = payload.Connection?.Protocol ?? _settings.ProxyProtocol,
            Host = payload.Connection?.Host,
            Port = payload.Connection?.Port,
            Username = payload.Connection?.Username,
            Password = payload.Connection?.Password,
            Country = _settings.ProxyCountry.ToUpperInvariant(),
            City = _settings.ProxyCity,
            Session = session,
        };
    }

    public async Task<IReadOnlyList<ProxyEndpoint>> AllocateAsync(
        int count,
        string sessionPrefix = "gelark",
        CancellationToken cancellationToken = default)
    {
        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "count must be >= 1");
        }

        var mode = _settings.ProxyMode.Trim().ToLowerInvariant();
        if (mode == "static")
        {
            var available = await ListStaticProxiesAsync(_settings.ProxyCountry, cancellationToken);
            if (available.Count < count)
            {
                throw new FloppyDataException(
                    $"Need {count} static FloppyData proxies in {_settings.ProxyCountry.ToUpperInvariant()}, found {available.Count}.");
            }

            return available.Take(count).ToList();
        }

        if (mode != "rotating")
        {
            throw new InvalidOperationException($"Unknown proxy mode: {_settings.ProxyMode}");
        }

        var allocated = new List<ProxyEndpoint>(count);
        for (var i = 1; i <= count; i++)
        {
            allocated.Add(await CreateRotatingConnectionAsync($"{sessionPrefix}-{i:000}", cancellationToken));
        }

        return allocated;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new FloppyDataException(FormatError(text, (int)response.StatusCode), (int)response.StatusCode);
        }

        return JsonSerializer.Deserialize<T>(text, JsonUtil.Options)
               ?? throw new FloppyDataException("FloppyData returned an empty JSON body");
    }

    private static string FormatError(string text, int statusCode)
    {
        try
        {
            var body = JsonSerializer.Deserialize<FloppyErrorBody>(text, JsonUtil.Options);
            var message = body?.Error?.Message ?? body?.Error?.Code ?? body?.Message;
            if (!string.IsNullOrWhiteSpace(message))
            {
                return $"FloppyData HTTP {statusCode}: {message}";
            }
        }
        catch (JsonException)
        {
        }

        return $"FloppyData HTTP {statusCode}";
    }

    private static string? ProtocolFromUrl(string connectionString)
    {
        var index = connectionString.IndexOf("://", StringComparison.Ordinal);
        return index <= 0 ? null : connectionString[..index];
    }
}
