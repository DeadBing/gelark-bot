using System.Text.Json;

namespace GelarkBot;

public sealed class ProfileCreator
{
    private readonly FloppyDataClient _floppyData;
    private readonly GeeLarkClient? _geeLark;
    private readonly AppSettings _settings;
    private readonly Func<string, CancellationToken, Task<string?>> _resolveIpv4;
    private readonly Func<ProxyEndpoint, CancellationToken, Task<ProxyCheckResult>> _liveProbe;

    public ProfileCreator(
        FloppyDataClient floppyData,
        GeeLarkClient? geeLark,
        AppSettings settings,
        Func<string, CancellationToken, Task<string?>>? resolveIpv4 = null,
        Func<ProxyEndpoint, CancellationToken, Task<ProxyCheckResult>>? liveProbe = null)
    {
        _floppyData = floppyData;
        _geeLark = geeLark;
        _settings = settings;
        _resolveIpv4 = resolveIpv4 ?? ProxyUrl.ResolveIpv4Async;
        _liveProbe = liveProbe ?? ProxyLiveProbe.ProbeAsync;
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
        var mobileTypes = MobileTypes.Expand(_settings.MobileType);
        var proxies = await _floppyData.AllocateAsync(request.Count, request.SessionPrefix, cancellationToken);
        var plans = ProfilePlanner.Build(
            proxies,
            emails,
            request.NamePrefix,
            _settings.Group,
            request.NameFromEmail,
            mobileTypes);

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

            profiles = request.CheckOnly
                ? await CheckOnlyAsync(plans, cancellationToken)
                : await PrepareAndCreateAsync(plans, cancellationToken);
        }

        var savedTo = request.DryRun
            ? SiblingOutputPath("last-dry-run.json")
            : request.CheckOnly
                ? SiblingOutputPath("last-proxy-check.json")
                : _settings.OutputFile;
        var result = new CreateResult
        {
            DryRun = request.DryRun,
            Total = profiles.Count,
            Success = profiles.Count(item => item.Ok),
            Failed = profiles.Count(item => !item.Ok),
            Profiles = profiles,
            SavedTo = savedTo,
        };

        if (request.DryRun || request.CheckOnly)
        {
            WriteResult(savedTo, result);
        }
        else
        {
            // The mapping file is the only place where account passwords and
            // TOTP secrets live, so runs merge into it instead of overwriting.
            MergeIntoFile(savedTo, result);
        }

        return result;
    }

    private string SiblingOutputPath(string fileName)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(_settings.OutputFile)) ?? "data";
        return Path.Combine(directory, fileName);
    }

    private async Task<IReadOnlyList<CreatedProfile>> CheckOnlyAsync(
        IReadOnlyList<ProfilePlan> plans,
        CancellationToken cancellationToken)
    {
        var profiles = new List<CreatedProfile>(plans.Count);
        foreach (var plan in plans)
        {
            var prepared = await PrepareAsync(plan, addToGeeLark: false, cancellationToken);
            profiles.Add(CreatedProfile.FromPlan(
                prepared.Plan,
                prepared.GeeLarkOk,
                error: prepared.GeeLarkOk ? null : prepared.Error));
        }

        return profiles;
    }

    private async Task<IReadOnlyList<CreatedProfile>> PrepareAndCreateAsync(
        IReadOnlyList<ProfilePlan> plans,
        CancellationToken cancellationToken)
    {
        var ready = new List<ProfilePlan>();
        var skipped = new Dictionary<int, CreatedProfile>();
        foreach (var (plan, index) in plans.Select((plan, index) => (plan, index)))
        {
            var prepared = await PrepareAsync(plan, addToGeeLark: true, cancellationToken);
            if (prepared.SkipCreate)
            {
                skipped[index] = CreatedProfile.FromPlan(prepared.Plan, false, error: prepared.Error);
                continue;
            }

            ready.Add(prepared.Plan);
        }

        var created = ready.Count == 0
            ? []
            : await _geeLark!.CreatePhonesAsync(ready, _settings.BatchSize, cancellationToken);

        var merged = new List<CreatedProfile>(plans.Count);
        var createdIndex = 0;
        for (var i = 0; i < plans.Count; i++)
        {
            if (skipped.TryGetValue(i, out var failed))
            {
                merged.Add(failed);
                continue;
            }

            var profile = created[createdIndex];
            var plan = ready[createdIndex];
            createdIndex++;
            if (!profile.Ok && GeeLarkCreateParser.IsProxyCheckFailure(profile.Error))
            {
                profile = CreatedProfile.FromPlan(
                    plan,
                    false,
                    phoneId: profile.Id,
                    envSerialNo: profile.EnvSerialNo,
                    error: GeeLarkCreateParser.ExplainProxyCheckFailure(plan),
                    equipment: profile.Equipment,
                    diagnostics: plan.Diagnostics);
            }
            else if (plan.Diagnostics.Count > 0)
            {
                profile = CreatedProfile.FromPlan(
                    plan,
                    profile.Ok,
                    phoneId: profile.Id,
                    envSerialNo: profile.EnvSerialNo,
                    error: profile.Error,
                    equipment: profile.Equipment,
                    diagnostics: plan.Diagnostics);
            }

            merged.Add(profile);
        }

        return merged;
    }

    private async Task<PreparedPlan> PrepareAsync(
        ProfilePlan plan,
        bool addToGeeLark,
        CancellationToken cancellationToken)
    {
        var parsed = ProxyUrl.Parse(plan.Proxy);
        var diagnostics = new List<string>
        {
            _settings.ProxyRotation == 0
                ? $"sticky session {parsed.Session ?? "-"} (rotation=0, one IP per profile)"
                : $"session {parsed.Session ?? "-"} rotation={_settings.ProxyRotation}",
        };

        var floppy = await SafeFloppyCheckAsync(parsed, cancellationToken);
        if (floppy is not null)
        {
            diagnostics.Add(floppy.ToString());
        }

        var local = await SafeLocalProbeAsync(parsed, cancellationToken);
        diagnostics.Add(local.ToString());

        var preferredName = _settings.ProxyQueryChannel == 2 ? "IP2Location" : "IP-API";
        var otherName = preferredName == "IP2Location" ? "IP-API" : "IP2Location";
        var preferredNum = preferredName == "IP2Location" ? 2 : 1;
        var otherNum = preferredNum == 2 ? 1 : 2;

        string? ipv4 = null;
        if (!string.IsNullOrWhiteSpace(parsed.Host) && !ProxyUrl.IsIpv4(parsed.Host))
        {
            ipv4 = await _resolveIpv4(parsed.Host, cancellationToken);
            if (!string.IsNullOrWhiteSpace(ipv4))
            {
                diagnostics.Add($"resolved {parsed.Host} -> {ipv4}");
            }
        }

        var attempts = new List<(ProxyEndpoint Proxy, string Channel, int ChannelNum)>();
        attempts.Add((parsed, preferredName, preferredNum));
        if (!string.IsNullOrWhiteSpace(ipv4) && !string.Equals(ipv4, parsed.Host, StringComparison.OrdinalIgnoreCase))
        {
            attempts.Add((ProxyUrl.WithServer(parsed, ipv4), preferredName, preferredNum));
        }

        attempts.Add((parsed, otherName, otherNum));
        if (!string.IsNullOrWhiteSpace(ipv4) && !string.Equals(ipv4, parsed.Host, StringComparison.OrdinalIgnoreCase))
        {
            attempts.Add((ProxyUrl.WithServer(parsed, ipv4), otherName, otherNum));
        }

        ProxyEndpoint chosen = parsed;
        var chosenChannel = preferredNum;
        var geeLarkOk = false;
        foreach (var attempt in attempts)
        {
            var check = await SafeGeeLarkCheckAsync(attempt.Proxy, attempt.Channel, cancellationToken);
            diagnostics.Add($"{check} {attempt.Proxy.Host}:{attempt.Proxy.Port} {attempt.Channel}");
            if (!check.Ok)
            {
                continue;
            }

            chosen = attempt.Proxy;
            chosenChannel = attempt.ChannelNum;
            geeLarkOk = true;
            break;
        }

        int? serial = null;
        if (addToGeeLark && geeLarkOk && _geeLark is not null)
        {
            try
            {
                serial = await _geeLark.AddOrGetSerialAsync(chosen, chosenChannel, cancellationToken);
                diagnostics.Add($"GeeLark proxy serial {serial}");
            }
            catch (GeeLarkException ex)
            {
                diagnostics.Add($"GeeLark add proxy skipped: {ex.Message}");
            }
        }

        var prepared = new ProfilePlan
        {
            ProfileName = plan.ProfileName,
            MobileType = plan.MobileType,
            Proxy = new ProxyEndpoint
            {
                ConnectionString = plan.Proxy.ConnectionString,
                Source = chosen.Source,
                Protocol = chosen.Protocol,
                Host = chosen.Host,
                Port = chosen.Port,
                Username = chosen.Username,
                Password = chosen.Password,
                Country = chosen.Country,
                City = chosen.City,
                Ip = chosen.Ip,
                Session = chosen.Session,
                StaticId = chosen.StaticId,
                GeeLarkSerial = serial,
            },
            Email = plan.Email,
            ProfileNote = plan.ProfileNote,
            ProfileTags = plan.ProfileTags,
            ProfileGroup = plan.ProfileGroup,
            ProxyNumber = serial,
            ProxyQueryChannel = chosenChannel,
            Diagnostics = diagnostics,
            LocalProbeOk = local.Ok,
            FloppyCheckOk = floppy?.Ok,
        };

        var error = geeLarkOk
            ? null
            : GeeLarkCreateParser.ExplainProxyCheckFailure(prepared);
        return new PreparedPlan(prepared, geeLarkOk, SkipCreate: false, error);
    }

    private async Task<ProxyCheckResult?> SafeFloppyCheckAsync(ProxyEndpoint proxy, CancellationToken cancellationToken)
    {
        try
        {
            return await _floppyData.CheckAsync(proxy, cancellationToken);
        }
        catch (Exception ex) when (ex is FloppyDataException or HttpRequestException or TaskCanceledException)
        {
            return new ProxyCheckResult { Source = "FloppyData", Ok = false, Message = ex.Message };
        }
    }

    private async Task<ProxyCheckResult> SafeLocalProbeAsync(ProxyEndpoint proxy, CancellationToken cancellationToken)
    {
        try
        {
            return await _liveProbe(proxy, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new ProxyCheckResult { Source = "local", Ok = false, Message = ex.Message };
        }
    }

    private async Task<ProxyCheckResult> SafeGeeLarkCheckAsync(
        ProxyEndpoint proxy,
        string channel,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _geeLark!.CheckProxyAsync(proxy, channel, cancellationToken);
        }
        catch (Exception ex) when (ex is GeeLarkException or HttpRequestException or TaskCanceledException)
        {
            return new ProxyCheckResult { Source = "GeeLark", Ok = false, Message = ex.Message };
        }
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

    internal static void MergeIntoFile(string path, CreateResult result)
    {
        WriteResult(path, Merge(ReadExisting(path), result));
    }

    private static CreateResult? ReadExisting(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CreateResult>(File.ReadAllText(path), JsonUtil.Options);
        }
        catch (JsonException)
        {
            // Never lose saved credentials to a corrupt file: keep it aside.
            File.Copy(path, path + ".bak", overwrite: true);
            return null;
        }
    }

    private static CreateResult Merge(CreateResult? existing, CreateResult current)
    {
        if (existing is null || existing.Profiles.Count == 0)
        {
            return current;
        }

        var currentIds = current.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id))
            .Select(profile => profile.Id!)
            .ToHashSet(StringComparer.Ordinal);
        var currentNames = current.Profiles
            .Select(profile => profile.ProfileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Entries with a phone id are only replaced by the same id; entries
        // without one (failed attempts) give way to a rerun with the same name.
        var kept = existing.Profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id)
                ? !currentIds.Contains(profile.Id!)
                : !currentNames.Contains(profile.ProfileName))
            .ToList();
        var merged = kept.Concat(current.Profiles).ToList();
        return new CreateResult
        {
            DryRun = false,
            Total = merged.Count,
            Success = merged.Count(profile => profile.Ok),
            Failed = merged.Count(profile => !profile.Ok),
            Profiles = merged,
            SavedTo = current.SavedTo,
        };
    }

    private sealed record PreparedPlan(ProfilePlan Plan, bool GeeLarkOk, bool SkipCreate, string? Error);
}
