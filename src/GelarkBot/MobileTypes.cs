using System.Text.RegularExpressions;

namespace GelarkBot;

/// <summary>
/// Expands the GEELARK_MOBILE_TYPE / --mobile-type spec into concrete GeeLark
/// mobileType values. Supported forms: "Android 12", "Android 12-16", "12-16",
/// and comma-separated lists of those. Ranges give every profile a random version.
/// </summary>
public static class MobileTypes
{
    private static readonly Regex VersionOrRange = new(
        @"^(?:android\s*)?(?<from>\d{1,2})(?:\s*-\s*(?:android\s*)?(?<to>\d{1,2}))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> Expand(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new FormatException("Mobile type is empty. Use e.g. \"Android 12\" or \"Android 12-16\".");
        }

        var expanded = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in spec.Split(','))
        {
            var item = raw.Trim();
            if (item.Length == 0)
            {
                continue;
            }

            var match = VersionOrRange.Match(item);
            if (!match.Success)
            {
                // Pass unknown values through untouched so future GeeLark
                // mobileType strings keep working without a bot update.
                Add(expanded, seen, item);
                continue;
            }

            var from = int.Parse(match.Groups["from"].Value);
            var to = match.Groups["to"].Success ? int.Parse(match.Groups["to"].Value) : from;
            if (to < from)
            {
                throw new FormatException($"Invalid mobile type range \"{item}\": {to} is lower than {from}.");
            }

            for (var version = from; version <= to; version++)
            {
                Add(expanded, seen, $"Android {version}");
            }
        }

        if (expanded.Count == 0)
        {
            throw new FormatException($"Mobile type \"{spec}\" has no usable values.");
        }

        return expanded;
    }

    public static string Pick(string spec, Random? random = null)
    {
        var all = Expand(spec);
        return all[(random ?? Random.Shared).Next(all.Count)];
    }

    private static void Add(List<string> expanded, HashSet<string> seen, string value)
    {
        if (seen.Add(value))
        {
            expanded.Add(value);
        }
    }
}
