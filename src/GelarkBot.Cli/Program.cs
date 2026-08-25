using System.CommandLine;
using System.Net.Http.Headers;
using GelarkBot;

DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

var countOption = new Option<int?>("--count", "-n")
{
    Description = "How many profiles to create. Defaults to the account-file size when --emails is set.",
};
var emailsOption = new Option<FileInfo?>("--emails")
{
    Description = "Optional account pool: login:password:totp. Not required to create a GeeLark profile.",
};
var dryRunOption = new Option<bool>("--dry-run")
{
    Description = "Fetch/build proxies and plan profiles, but do not call GeeLark create.",
};
var proxyModeOption = new Option<string?>("--proxy-mode")
{
    Description = "static or rotating. Default comes from PROXY_MODE.",
};
var proxyTypeOption = new Option<string?>("--proxy-type")
{
    Description = "residential, mobile, or datacenter. Used for rotating proxies.",
};
var countryOption = new Option<string?>("--country")
{
    Description = "ISO country code. Filters static inventory and targets rotating proxies.",
};
var protocolOption = new Option<string?>("--protocol")
{
    Description = "http, https, or socks5 for rotating proxies.",
};
var namePrefixOption = new Option<string>("--name-prefix")
{
    Description = "Profile name prefix when emails are not used for names.",
    DefaultValueFactory = _ => "gl",
};
var groupOption = new Option<string?>("--group")
{
    Description = "GeeLark profile group. Created automatically if missing.",
};
var mobileTypeOption = new Option<string?>("--mobile-type")
{
    Description = "Android version, for example \"Android 12\".",
};
var regionOption = new Option<string?>("--region")
{
    Description = "GeeLark region: cn, sgp, or us.",
};
var outputOption = new Option<string?>("--output")
{
    Description = "JSON mapping file for created profiles.",
};
var batchOption = new Option<int?>("--batch-size")
{
    Description = "GeeLark create batch size. Basic plans only allow 1. Default 1.",
};

var createCommand = new Command("create", "Create GeeLark cloud-phone profiles with FloppyData proxies")
{
    countOption,
    emailsOption,
    dryRunOption,
    proxyModeOption,
    proxyTypeOption,
    countryOption,
    protocolOption,
    namePrefixOption,
    groupOption,
    mobileTypeOption,
    regionOption,
    outputOption,
    batchOption,
};

createCommand.SetAction(async (parseResult, cancellationToken) =>
{
    try
    {
        var settings = AppSettings.FromEnvironment().With(
            proxyMode: parseResult.GetValue(proxyModeOption),
            proxyType: parseResult.GetValue(proxyTypeOption),
            proxyCountry: parseResult.GetValue(countryOption),
            proxyProtocol: parseResult.GetValue(protocolOption),
            mobileType: parseResult.GetValue(mobileTypeOption),
            region: parseResult.GetValue(regionOption),
            group: parseResult.GetValue(groupOption),
            outputFile: parseResult.GetValue(outputOption),
            batchSize: parseResult.GetValue(batchOption));

        settings.RequireFloppyData();
        var dryRun = parseResult.GetValue(dryRunOption);
        if (!dryRun)
        {
            settings.RequireGeeLark();
        }

        var emailsFile = parseResult.GetValue(emailsOption);
        IReadOnlyList<EmailCredential>? emails = null;
        if (emailsFile is not null)
        {
            emails = EmailPool.Load(emailsFile.FullName);
        }

        var count = parseResult.GetValue(countOption);
        if (count is null or 0)
        {
            if (emails is not { Count: > 0 })
            {
                Console.Error.WriteLine("Pass --count or --emails.");
                return 2;
            }

            count = emails.Count;
        }

        using var floppyHttp = CreateHttp(settings.TimeoutSeconds);
        using var geeLarkHttp = dryRun ? null : CreateHttp(settings.TimeoutSeconds);
        var floppy = new FloppyDataClient(floppyHttp, settings);
        var geeLark = geeLarkHttp is null ? null : new GeeLarkClient(geeLarkHttp, settings);
        var creator = new ProfileCreator(floppy, geeLark, settings);
        var result = await creator.CreateAsync(
            new CreateRequest
            {
                Count = count.Value,
                Emails = emails,
                DryRun = dryRun,
                NamePrefix = parseResult.GetValue(namePrefixOption) ?? "gl",
            },
            cancellationToken);

        foreach (var profile in result.Profiles)
        {
            var status = profile.Ok ? "OK" : "FAIL";
            var id = profile.Id ?? "-";
            var login = profile.Login ?? "-";
            Console.WriteLine($"{status}\t{profile.ProfileName}\t{id}\t{login}\t{NameUtil.RedactProxy(profile.Proxy)}");
            if (!profile.Ok && !string.IsNullOrWhiteSpace(profile.Error))
            {
                Console.WriteLine($"\t{profile.Error}");
            }
        }

        Console.WriteLine(
            $"{(result.DryRun ? "Planned" : "Created")} {result.Success}/{result.Total}. Saved {settings.OutputFile}");
        return result.Failed == 0 ? 0 : 1;
    }
    catch (Exception ex) when (ex is FloppyDataException or GeeLarkException or InvalidOperationException or FormatException or ArgumentOutOfRangeException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
});

var proxiesCommand = new Command("proxies", "List FloppyData static proxies");
proxiesCommand.SetAction(async (parseResult, cancellationToken) =>
{
    try
    {
        var settings = AppSettings.FromEnvironment();
        settings.RequireFloppyData();
        using var http = CreateHttp(settings.TimeoutSeconds);
        var client = new FloppyDataClient(http, settings);
        var country = string.IsNullOrWhiteSpace(settings.ProxyCountry) ? null : settings.ProxyCountry;
        var items = await client.ListStaticProxiesAsync(country, cancellationToken);
        foreach (var item in items)
        {
            Console.WriteLine($"{item.StaticId}\t{item.Country}\t{item.Ip}\t{NameUtil.RedactProxy(item.ConnectionString)}");
        }

        Console.WriteLine($"Total: {items.Count}");
        return 0;
    }
    catch (Exception ex) when (ex is FloppyDataException or InvalidOperationException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
});

var phonesCommand = new Command("phones", "List GeeLark cloud-phone profiles");
var pageOption = new Option<int>("--page") { DefaultValueFactory = _ => 1 };
var pageSizeOption = new Option<int>("--page-size") { DefaultValueFactory = _ => 20 };
phonesCommand.Options.Add(pageOption);
phonesCommand.Options.Add(pageSizeOption);
phonesCommand.SetAction(async (parseResult, cancellationToken) =>
{
    try
    {
        var settings = AppSettings.FromEnvironment();
        settings.RequireGeeLark();
        using var http = CreateHttp(settings.TimeoutSeconds);
        var client = new GeeLarkClient(http, settings);
        var data = await client.ListPhonesAsync(
            parseResult.GetValue(pageOption),
            parseResult.GetValue(pageSizeOption),
            cancellationToken);
        foreach (var item in data.Items)
        {
            Console.WriteLine($"{item.Id}\t{item.SerialName}\t{item.SerialNo}\t{item.Status}\t{item.Remark}");
        }

        Console.WriteLine($"Total: {data.Total}");
        return 0;
    }
    catch (Exception ex) when (ex is GeeLarkException or InvalidOperationException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
});

var root = new RootCommand("Create GeeLark cloud-phone profiles and attach FloppyData proxies")
{
    createCommand,
    proxiesCommand,
    phonesCommand,
};

return root.Parse(args).Invoke();

static HttpClient CreateHttp(int timeoutSeconds)
{
    var http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(timeoutSeconds),
    };
    http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return http;
}
