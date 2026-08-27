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
        return new EnvRow
        {
            ProfileName = plan.ProfileName,
            ProxyInformation = plan.Proxy.ConnectionString,
            MobileLanguage = _settings.Language,
            ProfileGroup = plan.ProfileGroup,
            ProfileTags = plan.ProfileTags.ToList(),
            ProfileNote = plan.ProfileNote,
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
