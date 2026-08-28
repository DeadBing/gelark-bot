using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GelarkBot;

public sealed class GeeLarkClient
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;

    public string? LastRawResponse { get; private set; }

    public GeeLarkClient(HttpClient http, AppSettings settings)
    {
        _settings = settings;
        _http = http;
        if (string.IsNullOrWhiteSpace(settings.GeeLarkToken) &&
            (string.IsNullOrWhiteSpace(settings.GeeLarkAppId) || string.IsNullOrWhiteSpace(settings.GeeLarkApiKey)))
        {
            throw new InvalidOperationException("GeeLark token or app_id+api_key is required");
        }
    }

    public async Task<IReadOnlyList<CreatedProfile>> CreatePhonesAsync(
        IReadOnlyList<ProfilePlan> plans,
        int batchSize = 1,
        CancellationToken cancellationToken = default)
    {
        if (plans.Count == 0)
        {
            return [];
        }

        batchSize = Math.Clamp(batchSize, 1, 100);
        var created = new List<CreatedProfile>();
        for (var start = 0; start < plans.Count; start += batchSize)
        {
            var chunk = plans.Skip(start).Take(batchSize).ToList();
            try
            {
                created.AddRange(await CreateBatchAsync(chunk, cancellationToken));
            }
            catch (GeeLarkException ex) when (ex.Code == 44001 && batchSize > 1)
            {
                created.AddRange(await CreatePhonesAsync(chunk, 1, cancellationToken));
            }
            catch (GeeLarkException ex)
            {
                created.AddRange(chunk.Select(plan => CreatedProfile.FromPlan(plan, false, error: ex.Message)));
            }
        }

        return created;
    }

    public async Task<PhoneListData> ListPhonesAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var payload = await PostAsync<PhoneListData>(
            "/open/v1/phone/list",
            new { page, pageSize },
            cancellationToken);
        if (payload.Code != 0)
        {
            throw new GeeLarkException($"GeeLark list failed: {payload.Msg ?? payload.Code.ToString()}", payload.Code);
        }

        return payload.Data ?? new PhoneListData();
    }

    public async Task<ProxyCheckResult> CheckProxyAsync(
        ProxyEndpoint proxy,
        string? queryChannel = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = ProxyUrl.Parse(proxy);
        var channel = queryChannel ?? (_settings.ProxyQueryChannel == 2 ? "IP2Location" : "IP-API");
        var body = new
        {
            proxyQueryChannel = channel,
            proxyType = string.IsNullOrWhiteSpace(parsed.Protocol) ? "http" : parsed.Protocol,
            server = parsed.Host,
            port = parsed.Port,
            username = parsed.Username,
            password = parsed.Password,
        };
        using var document = await PostDocumentAsync("/open/v1/proxy/check", body, cancellationToken);
        var root = document.RootElement;
        var topCode = root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var parsedCode)
            ? parsedCode
            : -1;
        var topMsg = root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
            ? msgEl.GetString()
            : null;
        if (topCode != 0 || !root.TryGetProperty("data", out var data))
        {
            return new ProxyCheckResult
            {
                Source = "GeeLark",
                Ok = false,
                Message = topMsg ?? $"code {topCode}",
            };
        }

        var ok = IsDetectOk(data);
        var message = data.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String
            ? messageEl.GetString()
            : topMsg;
        var ip = data.TryGetProperty("outboundIP", out var ipEl) ? ipEl.GetString() : null;
        var country = data.TryGetProperty("countryName", out var countryEl) ? countryEl.GetString() : null;
        return new ProxyCheckResult
        {
            Source = "GeeLark",
            Ok = ok,
            Ip = ip,
            Country = country,
            Message = ok ? null : string.IsNullOrWhiteSpace(message) ? "detectStatus=false" : message,
        };
    }

    public async Task<int> AddOrGetSerialAsync(
        ProxyEndpoint proxy,
        int? queryChannel = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = ProxyUrl.Parse(proxy);
        var body = new
        {
            list = new[]
            {
                new
                {
                    scheme = string.IsNullOrWhiteSpace(parsed.Protocol) ? "http" : parsed.Protocol,
                    server = parsed.Host,
                    port = parsed.Port,
                    username = parsed.Username,
                    password = parsed.Password,
                    proxyQueryChannel = queryChannel ?? _settings.ProxyQueryChannel,
                },
            },
        };
        using var document = await PostDocumentAsync("/open/v1/proxy/add", body, cancellationToken);
        var root = document.RootElement;
        var id = ReadAddedProxyId(root);
        if (!string.IsNullOrWhiteSpace(id))
        {
            var serial = await FindSerialAsync(id, parsed, cancellationToken);
            if (serial is int number)
            {
                return number;
            }
        }

        var existing = await FindSerialAsync(null, parsed, cancellationToken);
        if (existing is int found)
        {
            return found;
        }

        var msg = root.TryGetProperty("msg", out var msgEl) ? msgEl.GetString() : "add proxy failed";
        var code = root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var parsedCode)
            ? parsedCode
            : (int?)null;
        throw new GeeLarkException($"GeeLark add proxy failed: {msg}", code);
    }

    private static string? ReadAddedProxyId(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (data.TryGetProperty("successDetails", out var success) && success.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in success.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    return id.GetString();
                }
            }
        }

        return null;
    }

    private async Task<int?> FindSerialAsync(string? id, ProxyEndpoint proxy, CancellationToken cancellationToken)
    {
        var body = string.IsNullOrWhiteSpace(id)
            ? (object)new { page = 1, pageSize = 100 }
            : new { page = 1, pageSize = 10, ids = new[] { id } };
        var payload = await PostAsync<ProxyListData>("/open/v1/proxy/list", body, cancellationToken);
        if (payload.Code != 0)
        {
            return null;
        }

        var items = payload.Data?.List ?? [];
        var match = items.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(id) && item.Id == id) ||
            (string.Equals(item.Server, proxy.Host, StringComparison.OrdinalIgnoreCase) &&
             item.Port == proxy.Port &&
             string.Equals(item.Username, proxy.Username, StringComparison.Ordinal)));
        return match is { SerialNo: > 0 } found
            ? found.SerialNo
            : items.FirstOrDefault(item => item.SerialNo > 0)?.SerialNo;
    }

    private async Task<IReadOnlyList<CreatedProfile>> CreateBatchAsync(
        IReadOnlyList<ProfilePlan> plans,
        CancellationToken cancellationToken)
    {
        var body = new CreatePhonesBody
        {
            MobileType = _settings.MobileType,
            ChargeMode = _settings.ChargeMode,
            Region = _settings.Region,
            Data = plans.Select(ToEnvRow).ToList(),
        };

        using var document = await PostDocumentAsync("/open/v1/phone/addNew", body, cancellationToken);
        var root = document.RootElement;
        var topCode = root.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var parsedCode)
            ? parsedCode
            : -1;
        var topMsg = root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
            ? msgEl.GetString()
            : null;
        if (topCode is not (0 or 40006))
        {
            throw new GeeLarkException($"GeeLark create failed: {topMsg ?? topCode.ToString()}", topCode);
        }

        var details = root.TryGetProperty("data", out var data)
            ? GeeLarkCreateParser.ReadDetails(data)
            : [];
        return GeeLarkCreateParser.Map(plans, topCode, topMsg, details);
    }

    private EnvRow ToEnvRow(ProfilePlan plan)
    {
        var parsed = ProxyUrl.Parse(plan.Proxy);
        return new EnvRow
        {
            ProfileName = plan.ProfileName,
            ProxyInformation = plan.ProxyNumber is > 0 ? null : ProxyUrl.ForGeeLark(parsed),
            ProxyNumber = plan.ProxyNumber is > 0 ? plan.ProxyNumber : null,
            MobileLanguage = _settings.Language,
            ProfileGroup = plan.ProfileGroup,
            ProfileTags = plan.ProfileTags.ToList(),
            ProfileNote = plan.ProfileNote,
            ProxyQueryChannel = plan.ProxyQueryChannel ?? _settings.ProxyQueryChannel,
        };
    }

    private static bool IsDetectOk(JsonElement data)
    {
        if (!data.TryGetProperty("detectStatus", out var statusEl))
        {
            return false;
        }

        return statusEl.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => statusEl.TryGetInt32(out var value) && value == 1,
            JsonValueKind.String => string.Equals(statusEl.GetString(), "true", StringComparison.OrdinalIgnoreCase) ||
                                    statusEl.GetString() == "1",
            _ => false,
        };
    }

    private async Task<GeeLarkEnvelope<T>> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, HttpUrl.Combine(_settings.GeeLarkBaseUrl, path));
        foreach (var header in BuildHeaders())
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Content = JsonContent.Create(body, options: JsonUtil.Options);
        using var response = await _http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        LastRawResponse = text;
        GeeLarkEnvelope<T>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<GeeLarkEnvelope<T>>(text, JsonUtil.Options);
        }
        catch (JsonException)
        {
            throw new GeeLarkException($"GeeLark returned non-JSON ({(int)response.StatusCode}): {text[..Math.Min(text.Length, 300)]}");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new GeeLarkException($"GeeLark HTTP {(int)response.StatusCode}: {text}", payload?.Code);
        }

        return payload ?? throw new GeeLarkException("Unexpected GeeLark payload");
    }

    private async Task<JsonDocument> PostDocumentAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, HttpUrl.Combine(_settings.GeeLarkBaseUrl, path));
        foreach (var header in BuildHeaders())
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        request.Content = JsonContent.Create(body, options: JsonUtil.Options);
        using var response = await _http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        LastRawResponse = text;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
        catch (JsonException)
        {
            throw new GeeLarkException($"GeeLark returned non-JSON ({(int)response.StatusCode}): {text[..Math.Min(text.Length, 300)]}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var code = document.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var parsed)
                ? parsed
                : (int?)null;
            throw new GeeLarkException($"GeeLark HTTP {(int)response.StatusCode}: {text}", code);
        }

        return document;
    }

    private Dictionary<string, string> BuildHeaders()
    {
        var traceId = Guid.NewGuid().ToString("N").ToUpperInvariant();
        var headers = new Dictionary<string, string>
        {
            ["traceId"] = traceId,
        };

        if (!string.IsNullOrWhiteSpace(_settings.GeeLarkToken))
        {
            headers["Authorization"] = $"Bearer {_settings.GeeLarkToken}";
            return headers;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var nonce = traceId[..6];
        var signSource = $"{_settings.GeeLarkAppId}{traceId}{timestamp}{nonce}{_settings.GeeLarkApiKey}";
        var sign = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signSource)));
        headers["appId"] = _settings.GeeLarkAppId;
        headers["ts"] = timestamp;
        headers["nonce"] = nonce;
        headers["sign"] = sign;
        return headers;
    }
}
