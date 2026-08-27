using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace GelarkBot;

public static class ProxyUrl
{
    private static readonly Regex ColonFormat = new(
        @"^(?<scheme>https?|socks5)://(?<host>[^:/]+):(?<port>\d+):(?<user>[^:]+):(?<pass>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Func<string, CancellationToken, Task<string?>> ResolveIpv4Async { get; set; } = ResolveIpv4Core;

    public static ProxyEndpoint Parse(ProxyEndpoint proxy)
    {
        if (!string.IsNullOrWhiteSpace(proxy.Host) && proxy.Port is > 0 && !string.IsNullOrWhiteSpace(proxy.Username))
        {
            return proxy;
        }

        return Merge(proxy, ParseString(proxy.ConnectionString));
    }

    public static ProxyEndpoint? ParseString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var colon = ColonFormat.Match(connectionString);
        if (colon.Success)
        {
            return new ProxyEndpoint
            {
                ConnectionString = connectionString,
                Source = "",
                Protocol = colon.Groups["scheme"].Value.ToLowerInvariant(),
                Host = colon.Groups["host"].Value,
                Port = int.Parse(colon.Groups["port"].Value),
                Username = colon.Groups["user"].Value,
                Password = colon.Groups["pass"].Value,
            };
        }

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
        {
            return null;
        }

        string? username = null;
        string? password = null;
        if (!string.IsNullOrEmpty(uri.UserInfo) && uri.UserInfo.Contains(':'))
        {
            var split = uri.UserInfo.IndexOf(':');
            username = Uri.UnescapeDataString(uri.UserInfo[..split]);
            password = Uri.UnescapeDataString(uri.UserInfo[(split + 1)..]);
        }

        return new ProxyEndpoint
        {
            ConnectionString = connectionString,
            Source = "",
            Protocol = uri.Scheme,
            Host = uri.Host,
            Port = uri.IsDefaultPort ? null : uri.Port,
            Username = username,
            Password = password,
        };
    }

    public static string ForGeeLark(ProxyEndpoint proxy)
    {
        var parsed = Parse(proxy);
        if (string.IsNullOrWhiteSpace(parsed.Host) || parsed.Port is not > 0)
        {
            return parsed.ConnectionString;
        }

        var scheme = string.IsNullOrWhiteSpace(parsed.Protocol) ? "http" : parsed.Protocol;
        if (string.IsNullOrWhiteSpace(parsed.Username))
        {
            return $"{scheme}://{parsed.Host}:{parsed.Port}";
        }

        // GeeLark bulk-add / UI format. Their checker often misreads user:pass@host URLs
        // when the FloppyData username contains many hyphens.
        return $"{scheme}://{parsed.Host}:{parsed.Port}:{parsed.Username}:{parsed.Password}";
    }

    public static ProxyEndpoint WithServer(ProxyEndpoint proxy, string server)
    {
        var parsed = Parse(proxy);
        return new ProxyEndpoint
        {
            ConnectionString = parsed.ConnectionString,
            Source = parsed.Source,
            Protocol = parsed.Protocol,
            Host = server,
            Port = parsed.Port,
            Username = parsed.Username,
            Password = parsed.Password,
            Country = parsed.Country,
            City = parsed.City,
            Ip = parsed.Ip,
            Session = parsed.Session,
            StaticId = parsed.StaticId,
            GeeLarkSerial = parsed.GeeLarkSerial,
        };
    }

    public static bool IsIpv4(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        IPAddress.TryParse(host, out var address) &&
        address.AddressFamily == AddressFamily.InterNetwork;

    internal static async Task<string?> ResolveIpv4Core(string host, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        if (IsIpv4(host))
        {
            return host;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static ProxyEndpoint Merge(ProxyEndpoint proxy, ProxyEndpoint? parsed)
    {
        if (parsed is null)
        {
            return proxy;
        }

        return new ProxyEndpoint
        {
            ConnectionString = proxy.ConnectionString,
            Source = proxy.Source,
            Protocol = string.IsNullOrWhiteSpace(proxy.Protocol) ? parsed.Protocol : proxy.Protocol,
            Host = proxy.Host ?? parsed.Host,
            Port = proxy.Port ?? parsed.Port,
            Username = proxy.Username ?? parsed.Username,
            Password = proxy.Password ?? parsed.Password,
            Country = proxy.Country,
            City = proxy.City,
            Ip = proxy.Ip,
            Session = proxy.Session,
            StaticId = proxy.StaticId,
            GeeLarkSerial = proxy.GeeLarkSerial,
        };
    }
}
