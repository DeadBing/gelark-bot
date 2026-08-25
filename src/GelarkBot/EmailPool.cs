namespace GelarkBot;

public static class EmailPool
{
    public static EmailCredential? ParseLine(string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#'))
        {
            return null;
        }

        var parts = text.Split(':');
        var login = parts[0].Trim();
        if (login.Length == 0)
        {
            throw new FormatException($"Invalid account line: {line}");
        }

        string? password = null;
        string? totpSecret = null;
        if (parts.Length == 2)
        {
            password = EmptyToNull(parts[1]);
        }
        else if (parts.Length >= 3)
        {
            totpSecret = EmptyToNull(parts[^1]);
            password = EmptyToNull(string.Join(':', parts[1..^1]));
        }

        return new EmailCredential
        {
            Login = login,
            Password = password,
            TotpSecret = totpSecret,
        };
    }

    public static IReadOnlyList<EmailCredential> Load(string path)
    {
        var accounts = new List<EmailCredential>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = File.ReadAllLines(path);
        for (var i = 0; i < lines.Length; i++)
        {
            EmailCredential? item;
            try
            {
                item = ParseLine(lines[i]);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"{path}:{i + 1}: {ex.Message}", ex);
            }

            if (item is null)
            {
                continue;
            }

            if (!seen.Add(item.Login))
            {
                throw new FormatException($"{path}:{i + 1}: duplicate login {item.Login}");
            }

            accounts.Add(item);
        }

        return accounts;
    }

    public static IReadOnlyList<EmailCredential> Take(IReadOnlyList<EmailCredential> pool, int count)
    {
        if (count > pool.Count)
        {
            throw new InvalidOperationException($"Need {count} accounts, but the pool only has {pool.Count}.");
        }

        return pool.Take(count).ToList();
    }

    private static string? EmptyToNull(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
