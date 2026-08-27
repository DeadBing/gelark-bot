namespace GelarkBot;

public sealed class ProxyCheckResult
{
    public required string Source { get; init; }
    public required bool Ok { get; init; }
    public string? Ip { get; init; }
    public string? Country { get; init; }
    public string? Message { get; init; }

    public override string ToString()
    {
        if (Ok)
        {
            return $"{Source} OK {Ip ?? "-"} {Country ?? ""}".Trim();
        }

        return $"{Source} FAIL {Message ?? "check failed"}";
    }
}
