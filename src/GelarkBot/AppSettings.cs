namespace GelarkBot;

public sealed class AppSettings
{
    public string GeeLarkToken { get; init; } = "";
    public string GeeLarkAppId { get; init; } = "";
    public string GeeLarkApiKey { get; init; } = "";
    public string GeeLarkBaseUrl { get; init; } = "https://openapi.geelark.com";

    public string FloppyDataApiKey { get; init; } = "";
    public string FloppyDataBaseUrl { get; init; } = "https://api.floppydata.net";

    public string ProxyMode { get; init; } = "rotating";
    public string ProxyType { get; init; } = "mobile";
    public string ProxyCountry { get; init; } = "US";
    public string? ProxyCity { get; init; }
    public string? ProxyState { get; init; }
    public string ProxyProtocol { get; init; } = "http";
    public int ProxyRotation { get; init; } = 0;
    public int ProxyQueryChannel { get; init; } = 1;

    public string MobileType { get; init; } = "Android 12-16";
    public string Region { get; init; } = "sgp";
    public int ChargeMode { get; init; } = 0;
    public string? Group { get; init; }
    public string Language { get; init; } = "baseOnIP";

    public string OutputFile { get; init; } = "data/created-profiles.json";
    public int BatchSize { get; init; } = 1;
    public int TimeoutSeconds { get; init; } = 60;

    public static AppSettings FromEnvironment()
    {
        return new AppSettings
        {
            GeeLarkToken = Get("GEELARK_TOKEN"),
            GeeLarkAppId = Get("GEELARK_APP_ID"),
            GeeLarkApiKey = Get("GEELARK_API_KEY"),
            GeeLarkBaseUrl = Get("GEELARK_BASE_URL", "https://openapi.geelark.com"),
            FloppyDataApiKey = Get("FLOPPYDATA_API_KEY"),
            FloppyDataBaseUrl = Get("FLOPPYDATA_BASE_URL", "https://api.floppydata.net"),
            ProxyMode = Get("PROXY_MODE", "rotating"),
            ProxyType = Get("PROXY_TYPE", "mobile"),
            ProxyCountry = Get("PROXY_COUNTRY", "US"),
            ProxyCity = EmptyToNull(Get("PROXY_CITY")),
            ProxyState = EmptyToNull(Get("PROXY_STATE")),
            ProxyProtocol = Get("PROXY_PROTOCOL", "http"),
            ProxyRotation = GetInt("PROXY_ROTATION", 0),
            ProxyQueryChannel = GetInt("GEELARK_PROXY_QUERY_CHANNEL", 1),
            MobileType = Get("GEELARK_MOBILE_TYPE", "Android 12-16"),
            Region = Get("GEELARK_REGION", "sgp"),
            ChargeMode = GetInt("GEELARK_CHARGE_MODE", 0),
            Group = EmptyToNull(Get("GEELARK_GROUP")),
            Language = Get("GEELARK_LANGUAGE", "baseOnIP"),
            OutputFile = Get("OUTPUT_FILE", "data/created-profiles.json"),
            BatchSize = GetInt("GEELARK_BATCH_SIZE", 1),
            TimeoutSeconds = GetInt("HTTP_TIMEOUT_SECONDS", 60),
        };
    }

    public AppSettings With(
        string? proxyMode = null,
        string? proxyType = null,
        string? proxyCountry = null,
        string? proxyCity = null,
        string? proxyState = null,
        string? proxyProtocol = null,
        int? proxyRotation = null,
        string? mobileType = null,
        string? region = null,
        int? chargeMode = null,
        string? group = null,
        string? language = null,
        string? outputFile = null,
        int? batchSize = null)
    {
        return new AppSettings
        {
            GeeLarkToken = GeeLarkToken,
            GeeLarkAppId = GeeLarkAppId,
            GeeLarkApiKey = GeeLarkApiKey,
            GeeLarkBaseUrl = GeeLarkBaseUrl,
            FloppyDataApiKey = FloppyDataApiKey,
            FloppyDataBaseUrl = FloppyDataBaseUrl,
            ProxyMode = proxyMode ?? ProxyMode,
            ProxyType = proxyType ?? ProxyType,
            ProxyCountry = proxyCountry ?? ProxyCountry,
            ProxyCity = proxyCity ?? ProxyCity,
            ProxyState = proxyState ?? ProxyState,
            ProxyProtocol = proxyProtocol ?? ProxyProtocol,
            ProxyRotation = proxyRotation ?? ProxyRotation,
            ProxyQueryChannel = ProxyQueryChannel,
            MobileType = mobileType ?? MobileType,
            Region = region ?? Region,
            ChargeMode = chargeMode ?? ChargeMode,
            Group = group ?? Group,
            Language = language ?? Language,
            OutputFile = outputFile ?? OutputFile,
            BatchSize = batchSize ?? BatchSize,
            TimeoutSeconds = TimeoutSeconds,
        };
    }

    public void RequireGeeLark()
    {
        if (!string.IsNullOrWhiteSpace(GeeLarkToken))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(GeeLarkAppId) && !string.IsNullOrWhiteSpace(GeeLarkApiKey))
        {
            return;
        }

        throw new InvalidOperationException("Set GEELARK_TOKEN or both GEELARK_APP_ID and GEELARK_API_KEY.");
    }

    public void RequireFloppyData()
    {
        if (string.IsNullOrWhiteSpace(FloppyDataApiKey))
        {
            throw new InvalidOperationException("Set FLOPPYDATA_API_KEY.");
        }
    }

    public string DescribeProxy()
    {
        var mode = ProxyMode.Trim().ToLowerInvariant();
        if (mode == "rotating")
        {
            var hold = ProxyRotation == 0
                ? "sticky session (rotation=0, IP holds per profile)"
                : ProxyRotation < 0
                    ? "rotate every request"
                    : $"rotate every {ProxyRotation} min";
            return $"FloppyData {ProxyType} {ProxyCountry.ToUpperInvariant()} {hold}, GB balance, {ProxyProtocol}";
        }

        var country = string.IsNullOrWhiteSpace(ProxyCountry) ? "any country" : ProxyCountry.ToUpperInvariant();
        return $"FloppyData static inventory ({country})";
    }

    private static string Get(string key, string fallback = "")
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int GetInt(string key, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
