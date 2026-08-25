using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GelarkBot;

public sealed class GeeLarkClient
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;

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

        var payload = await PostAsync<CreatePhonesData>("/open/v1/phone/addNew", body, cancellationToken);
        if (payload.Code is not (0 or 40006))
        {
            throw new GeeLarkException($"GeeLark create failed: {payload.Msg ?? payload.Code.ToString()}", payload.Code);
        }

        var details = payload.Data?.Details ?? [];
        var byName = details
            .Where(item => !string.IsNullOrWhiteSpace(item.ProfileName))
            .ToDictionary(item => item.ProfileName!, item => item, StringComparer.Ordinal);
        var byIndex = details
            .Where(item => item.Index != 0)
            .ToDictionary(item => item.Index, item => item);

        var results = new List<CreatedProfile>();
        for (var offset = 0; offset < plans.Count; offset++)
        {
            var plan = plans[offset];
            if (!byName.TryGetValue(plan.ProfileName, out var item) &&
                !byIndex.TryGetValue(offset + 1, out item))
            {
                results.Add(CreatedProfile.FromPlan(plan, false, error: payload.Msg ?? "GeeLark did not return this profile"));
                continue;
            }

            if (item.Code == 0)
            {
                results.Add(CreatedProfile.FromPlan(
                    plan,
                    true,
                    item.Id,
                    item.EnvSerialNo,
                    equipment: item.EquipmentInfo));
                continue;
            }

            results.Add(CreatedProfile.FromPlan(plan, false, error: item.Msg ?? item.Code.ToString()));
        }

        return results;
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
