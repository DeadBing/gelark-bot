using System.Text.Json;

namespace GelarkBot;

internal static class GeeLarkCreateParser
{
    public static IReadOnlyList<CreatedProfile> Map(
        IReadOnlyList<ProfilePlan> plans,
        int topCode,
        string? topMsg,
        IReadOnlyList<CreatePhoneDetail> details)
    {
        var used = new bool[details.Count];
        var results = new List<CreatedProfile>(plans.Count);
        for (var offset = 0; offset < plans.Count; offset++)
        {
            var plan = plans[offset];
            var index = FindDetail(plan, offset, details, used);
            if (index < 0)
            {
                var hint = topCode is 0 or 40006
                    ? "GeeLark returned success but no detail for this profile. Run `phones` — it may already exist."
                    : topMsg ?? $"GeeLark create failed ({topCode})";
                results.Add(CreatedProfile.FromPlan(plan, false, error: hint));
                continue;
            }

            used[index] = true;
            var item = details[index];
            if (IsDetailSuccess(item))
            {
                results.Add(CreatedProfile.FromPlan(
                    plan,
                    true,
                    item.PhoneId,
                    item.ReturnedSerial,
                    equipment: item.EquipmentInfo));
                continue;
            }

            var error = item.Msg;
            if (string.IsNullOrWhiteSpace(error) || IsSuccessMessage(error))
            {
                error = $"GeeLark detail code {item.Code}";
            }

            if (IsProxyCheckFailure(error))
            {
                error =
                    "check proxy failed: GeeLark could not probe this FloppyData endpoint. " +
                    "Their checker often fails on SOCKS5 geo.g-w.info:10800. Retry with --protocol http.";
            }

            results.Add(CreatedProfile.FromPlan(plan, false, error: error, phoneId: item.PhoneId));
        }

        return results;
    }

    public static List<CreatePhoneDetail> ReadDetails(JsonElement data)
    {
        foreach (var name in new[] { "details", "detail", "items", "list", "records" })
        {
            if (data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty(name, out var array) &&
                array.ValueKind == JsonValueKind.Array)
            {
                return DeserializeDetails(array);
            }
        }

        if (data.ValueKind == JsonValueKind.Array)
        {
            return DeserializeDetails(data);
        }

        if (data.ValueKind == JsonValueKind.Object && HasId(data))
        {
            return DeserializeDetails(JsonSerializer.SerializeToElement(new[] { data }, JsonUtil.Options));
        }

        return [];
    }

    private static int FindDetail(
        ProfilePlan plan,
        int offset,
        IReadOnlyList<CreatePhoneDetail> details,
        bool[] used)
    {
        for (var i = 0; i < details.Count; i++)
        {
            if (used[i])
            {
                continue;
            }

            if (string.Equals(details[i].ReturnedName, plan.ProfileName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        for (var i = 0; i < details.Count; i++)
        {
            if (used[i] || details[i].Index is not int index)
            {
                continue;
            }

            if (index == offset + 1 || index == offset)
            {
                return i;
            }
        }

        if (details.Count > offset && !used[offset])
        {
            return offset;
        }

        return -1;
    }

    private static bool IsDetailSuccess(CreatePhoneDetail item)
    {
        if (item.Code is 0 or 40006)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(item.PhoneId) &&
               (item.Code is 1 || IsSuccessMessage(item.Msg));
    }

    internal static bool IsProxyCheckFailure(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("check proxy failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuccessMessage(string? message) =>
        string.Equals(message?.Trim(), "success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(message?.Trim(), "ok", StringComparison.OrdinalIgnoreCase);

    private static bool HasId(JsonElement data) =>
        (data.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString()?.Length > 0) ||
        (data.TryGetProperty("envId", out var envId) && envId.ValueKind == JsonValueKind.String && envId.GetString()?.Length > 0);

    private static List<CreatePhoneDetail> DeserializeDetails(JsonElement array) =>
        JsonSerializer.Deserialize<List<CreatePhoneDetail>>(array.GetRawText(), JsonUtil.Options) ?? [];
}
