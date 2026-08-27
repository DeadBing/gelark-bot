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

    public async Task<StaticInventory> ListStaticInventoryAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, HttpUrl.Combine(_settings.FloppyDataBaseUrl, "/v2/proxy/static"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.FloppyDataApiKey);
        using var response = await _http.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<StaticProxyList>(response, cancellationToken);

        var proxies = new List<ProxyEndpoint>();
        foreach (var item in payload.Items)
        {
            var connectionString = ConnectionStringOf(item);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                continue;
            }

            var countryCode = FirstNonEmpty(item.CountryCode, item.Country)?.ToUpperInvariant();
            proxies.Add(new ProxyEndpoint
            {
                ConnectionString = connectionString,
                Source = "static",
                Protocol = ProtocolFromUrl(connectionString) ?? item.Connection?.Protocol,
                Host = item.Connection?.Host,
                Port = item.Connection is { Port: > 0 } ? item.Connection.Port : null,
                Username = item.Connection?.Username,
                Password = item.Connection?.Password,
                Country = countryCode,
                Ip = item.Ip,
                StaticId = item.Id,
            });
        }

        return new StaticInventory
        {
            Items = proxies,
            PendingCount = payload.PendingCount,
        };
    }

    public async Task<IReadOnlyList<ProxyEndpoint>> ListStaticProxiesAsync(
        string? country = null,
        CancellationToken cancellationToken = default)
    {
        var inventory = await ListStaticInventoryAsync(cancellationToken);
        return FilterByCountry(inventory.Items, country);
    }

    public async Task<ProxyEndpoint> CreateRotatingConnectionAsync(
        string session,
        string? protocol = null,
        CancellationToken cancellationToken = default)
    {
        var usedProtocol = protocol ?? _settings.ProxyProtocol;
        var body = new RotatingConnectionRequest
        {
            Type = _settings.ProxyType,
            Country = _settings.ProxyCountry.ToUpperInvariant(),
            Protocol = usedProtocol,
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
            Protocol = payload.Connection?.Protocol ?? usedProtocol,
            Host = payload.Connection?.Host,
            Port = payload.Connection?.Port,
            Username = payload.Connection?.Username,
            Password = payload.Connection?.Password,
            Country = _settings.ProxyCountry.ToUpperInvariant(),
            City = _settings.ProxyCity,
            Session = session,
        };
    }

    public async Task<ProxyCheckResult> CheckAsync(ProxyEndpoint proxy, CancellationToken cancellationToken = default)
    {
        var parsed = ProxyUrl.Parse(proxy);
        object body;
        if (!string.IsNullOrWhiteSpace(parsed.Host) && parsed.Port is > 0 && !string.IsNullOrWhiteSpace(parsed.Username))
        {
            body = new
            {
                host = parsed.Host,
                port = parsed.Port,
                username = parsed.Username,
                password = parsed.Password ?? "",
                protocol = parsed.Protocol ?? "http",
            };
        }
        else
        {
            body = new { connectionString = parsed.ConnectionString };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, HttpUrl.Combine(_settings.FloppyDataBaseUrl, "/v2/proxy/check"));
        request.Headers.TryAddWithoutValidation("X-Api-Key", _settings.FloppyDataApiKey);
        request.Content = JsonContent.Create(body, options: JsonUtil.Options);
        using var response = await _http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ProxyCheckResult
            {
                Source = "FloppyData",
                Ok = false,
                Message = FormatError(text, (int)response.StatusCode),
            };
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        var root = document.RootElement;
        var ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() : null;
        string? country = null;
        if (root.TryGetProperty("location", out var location) && location.ValueKind == JsonValueKind.Object)
        {
            country = location.TryGetProperty("countryCode", out var cc) ? cc.GetString() : null;
        }

        return new ProxyCheckResult
        {
            Source = "FloppyData",
            Ok = !string.IsNullOrWhiteSpace(ip),
            Ip = ip,
            Country = country,
            Message = string.IsNullOrWhiteSpace(ip) ? "no exit IP" : null,
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
            var inventory = await ListStaticInventoryAsync(cancellationToken);
            var available = FilterByCountry(inventory.Items, _settings.ProxyCountry);
            if (available.Count < count)
            {
                throw new FloppyDataException(
                    FormatStaticShortage(count, _settings.ProxyCountry, inventory));
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
            allocated.Add(await CreateRotatingConnectionAsync($"{sessionPrefix}-{i:000}", cancellationToken: cancellationToken));
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

    private static IReadOnlyList<ProxyEndpoint> FilterByCountry(
        IReadOnlyList<ProxyEndpoint> items,
        string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return items;
        }

        return items
            .Where(item => string.Equals(item.Country, country, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    internal static string FormatStaticShortage(int needed, string? country, StaticInventory inventory)
    {
        var matched = FilterByCountry(inventory.Items, country);
        var where = string.IsNullOrWhiteSpace(country) ? "" : $" in {country.Trim().ToUpperInvariant()}";
        var lines = new List<string>
        {
            $"Need {needed} static FloppyData proxies{where}, found {matched.Count}.",
        };

        if (inventory.Items.Count == 0)
        {
            lines.Add(
                inventory.PendingCount > 0
                    ? $"This account has no assigned static IPs yet ({inventory.PendingCount} still pending)."
                    : "This account has no assigned static IPs.");
            lines.Add("If you bought rotating traffic (GB), run with --proxy-mode rotating.");
            return string.Join(" ", lines);
        }

        var byCountry = inventory.Items
            .GroupBy(item => item.Country ?? "?")
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}:{group.Count()}");
        lines.Add($"Static inventory: {inventory.Items.Count} ({string.Join(", ", byCountry)}).");
        if (inventory.PendingCount > 0)
        {
            lines.Add($"Pending: {inventory.PendingCount}.");
        }

        lines.Add("Use --country <code> from the inventory, or --proxy-mode rotating.");
        return string.Join(" ", lines);
    }

    private static string? ConnectionStringOf(StaticProxyItem item)
    {
        var raw = item.Connection?.ConnectionString;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var connection = item.Connection;
        if (connection is null || string.IsNullOrWhiteSpace(connection.Host) || connection.Port <= 0)
        {
            return null;
        }

        var protocol = string.IsNullOrWhiteSpace(connection.Protocol) ? "http" : connection.Protocol;
        if (string.IsNullOrWhiteSpace(connection.Username))
        {
            return $"{protocol}://{connection.Host}:{connection.Port}";
        }

        return $"{protocol}://{connection.Username}:{connection.Password}@{connection.Host}:{connection.Port}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? ProtocolFromUrl(string connectionString)
    {
        var index = connectionString.IndexOf("://", StringComparison.Ordinal);
        return index <= 0 ? null : connectionString[..index];
    }
}
