using System.Text.Json;

namespace GelarkBot;

public sealed class ProfileCreator
{
    private readonly FloppyDataClient _floppyData;
    private readonly GeeLarkClient? _geeLark;
    private readonly AppSettings _settings;

    public ProfileCreator(FloppyDataClient floppyData, GeeLarkClient? geeLark, AppSettings settings)
    {
        _floppyData = floppyData;
        _geeLark = geeLark;
        _settings = settings;
    }

    public async Task<CreateResult> CreateAsync(CreateRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Count < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Count must be >= 1");
        }

        var emails = request.Emails is { Count: > 0 }
            ? EmailPool.Take(request.Emails, request.Count)
            : null;
        var proxies = await _floppyData.AllocateAsync(request.Count, request.SessionPrefix, cancellationToken);
        var plans = ProfilePlanner.Build(proxies, emails, request.NamePrefix, _settings.Group, request.NameFromEmail);

        IReadOnlyList<CreatedProfile> profiles;
        if (request.DryRun)
        {
            profiles = plans.Select(plan => CreatedProfile.FromPlan(plan, true)).ToList();
        }
        else
        {
            if (_geeLark is null)
            {
                throw new InvalidOperationException("GeeLark client is required unless --dry-run is set.");
            }

            profiles = await _geeLark.CreatePhonesAsync(plans, _settings.BatchSize, cancellationToken);
            profiles = await RetryProxyCheckFailuresAsync(plans, profiles, cancellationToken);
        }

        var result = new CreateResult
        {
            DryRun = request.DryRun,
            Total = profiles.Count,
            Success = profiles.Count(item => item.Ok),
            Failed = profiles.Count(item => !item.Ok),
            Profiles = profiles,
        };

        WriteResult(_settings.OutputFile, result);
        return result;
    }

    private async Task<IReadOnlyList<CreatedProfile>> RetryProxyCheckFailuresAsync(
        IReadOnlyList<ProfilePlan> plans,
        IReadOnlyList<CreatedProfile> profiles,
        CancellationToken cancellationToken)
    {
        if (_geeLark is null || _settings.ProxyMode.Trim().ToLowerInvariant() != "rotating")
        {
            return profiles;
        }

        var retried = profiles.ToList();
        for (var i = 0; i < retried.Count; i++)
        {
            var profile = retried[i];
            var plan = plans[i];
            if (!GeeLarkCreateParser.IsProxyCheckFailure(profile.Error))
            {
                continue;
            }

            if (!string.Equals(plan.Proxy.Protocol, "socks5", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var session = plan.Proxy.Session ?? $"retry-{i + 1:000}";
            var httpProxy = await _floppyData.CreateRotatingConnectionAsync(session, "http", cancellationToken);
            var httpPlan = new ProfilePlan
            {
                ProfileName = plan.ProfileName,
                Proxy = httpProxy,
                Email = plan.Email,
                ProfileNote = plan.ProfileNote,
                ProfileTags = plan.ProfileTags,
                ProfileGroup = plan.ProfileGroup,
            };
            var again = await _geeLark.CreatePhonesAsync([httpPlan], 1, cancellationToken);
            if (again.Count > 0)
            {
                retried[i] = again[0];
            }
        }

        return retried;
    }

    public static void WriteResult(string path, CreateResult result)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonUtil.Indented));
    }
}
