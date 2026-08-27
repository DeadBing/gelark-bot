using System.Text.Json.Serialization;

namespace GelarkBot;

public sealed class EmailCredential
{
    public required string Login { get; init; }
    public string? Password { get; init; }
    public string? TotpSecret { get; init; }

    public string NoteLine() => $"login: {Login}";
}

public sealed class ProxyEndpoint
{
    public required string ConnectionString { get; init; }
    public required string Source { get; init; }
    public string? Protocol { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Country { get; init; }
    public string? City { get; init; }
    public string? Ip { get; init; }
    public string? Session { get; init; }
    public int? StaticId { get; init; }
}

public sealed class ProfilePlan
{
    public required string ProfileName { get; init; }
    public required ProxyEndpoint Proxy { get; init; }
    public EmailCredential? Email { get; init; }
    public string ProfileNote { get; init; } = "";
    public IReadOnlyList<string> ProfileTags { get; init; } = [];
    public string? ProfileGroup { get; init; }
}

public sealed class CreatedProfile
{
    public required string ProfileName { get; init; }
    public required bool Ok { get; init; }
    public string? Id { get; init; }
    public string? EnvSerialNo { get; init; }
    public string? Login { get; init; }
    public string? Password { get; init; }
    public string? TotpSecret { get; init; }
    public required string Proxy { get; init; }
    public required string ProxySource { get; init; }
    public string Note { get; init; } = "";
    public string? Error { get; init; }
    public Dictionary<string, object?> Equipment { get; init; } = new();

    public static CreatedProfile FromPlan(
        ProfilePlan plan,
        bool ok,
        string? phoneId = null,
        string? envSerialNo = null,
        string? error = null,
        Dictionary<string, object?>? equipment = null)
    {
        return new CreatedProfile
        {
            ProfileName = plan.ProfileName,
            Ok = ok,
            Id = phoneId,
            EnvSerialNo = envSerialNo,
            Login = plan.Email?.Login,
            Password = plan.Email?.Password,
            TotpSecret = plan.Email?.TotpSecret,
            Proxy = plan.Proxy.ConnectionString,
            ProxySource = plan.Proxy.Source,
            Note = plan.ProfileNote,
            Error = error,
            Equipment = equipment ?? new Dictionary<string, object?>(),
        };
    }
}

public sealed class CreateResult
{
    public bool DryRun { get; init; }
    public int Total { get; init; }
    public int Success { get; init; }
    public int Failed { get; init; }
    public required IReadOnlyList<CreatedProfile> Profiles { get; init; }
}

public sealed class CreateRequest
{
    public required int Count { get; init; }
    public IReadOnlyList<EmailCredential>? Emails { get; init; }
    public bool DryRun { get; init; }
    public string NamePrefix { get; init; } = "gl";
    public bool NameFromEmail { get; init; } = true;
    public string SessionPrefix { get; init; } = "gelark";
}

public sealed class GeeLarkException : Exception
{
    public int? Code { get; }

    public GeeLarkException(string message, int? code = null) : base(message)
    {
        Code = code;
    }
}

public sealed class FloppyDataException : Exception
{
    public int? StatusCode { get; }

    public FloppyDataException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}

internal sealed class GeeLarkEnvelope<T>
{
    public string? TraceId { get; set; }
    public int Code { get; set; }
    public string? Msg { get; set; }
    public T? Data { get; set; }
}

internal sealed class CreatePhonesData
{
    public int TotalAmount { get; set; }
    public int SuccessAmount { get; set; }
    public int FailAmount { get; set; }
    public List<CreatePhoneDetail> Details { get; set; } = [];
}

internal sealed class CreatePhoneDetail
{
    public int Index { get; set; }
    public int Code { get; set; }
    public string? Msg { get; set; }
    public string? Id { get; set; }
    public string? ProfileName { get; set; }
    public string? EnvSerialNo { get; set; }
    public Dictionary<string, object?>? EquipmentInfo { get; set; }
}

public sealed class PhoneListData
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<PhoneListItem> Items { get; set; } = [];
}

public sealed class PhoneListItem
{
    public string? Id { get; set; }
    public string? SerialName { get; set; }
    public string? SerialNo { get; set; }
    public string? Remark { get; set; }
    public int Status { get; set; }
}

internal sealed class CreatePhonesBody
{
    public required string MobileType { get; set; }
    public int ChargeMode { get; set; }
    public required string Region { get; set; }
    public required List<EnvRow> Data { get; set; }
}

internal sealed class EnvRow
{
    public required string ProfileName { get; set; }
    public required string ProxyInformation { get; set; }
    public string? MobileLanguage { get; set; }
    public string? ProfileGroup { get; set; }
    public List<string>? ProfileTags { get; set; }
    public string? ProfileNote { get; set; }
}

internal sealed class FloppyErrorBody
{
    public FloppyErrorDetail? Error { get; set; }
    public string? Message { get; set; }
}

internal sealed class FloppyErrorDetail
{
    public string? Code { get; set; }
    public string? Message { get; set; }
}

internal sealed class StaticProxyList
{
    public List<StaticProxyItem> Items { get; set; } = [];
    public int PendingCount { get; set; }
}

internal sealed class StaticProxyItem
{
    public int Id { get; set; }
    public string? Ip { get; set; }
    public string? ProxyType { get; set; }
    public string? CountryCode { get; set; }
    public string? Country { get; set; }
    public string? CountryName { get; set; }
    public FloppyConnection? Connection { get; set; }
}

public sealed class StaticInventory
{
    public IReadOnlyList<ProxyEndpoint> Items { get; init; } = [];
    public int PendingCount { get; init; }
}

internal sealed class FloppyConnection
{
    public string? Protocol { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ConnectionString { get; set; }
}

internal sealed class RotatingConnectionResponse
{
    public FloppyConnection? Connection { get; set; }
}

internal sealed class RotatingConnectionRequest
{
    public required string Type { get; set; }
    public required string Country { get; set; }
    public required string Protocol { get; set; }
    public int Rotation { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? City { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Session { get; set; }
}
