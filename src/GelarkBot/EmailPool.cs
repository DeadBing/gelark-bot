using System.Text.RegularExpressions;

namespace GelarkBot;

public static class EmailPool
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static EmailCredential? ParseLine(string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#'))
        {
            return null;
        }

        var email = text;
        string? password = null;
        foreach (var separator in new[] { ':', ',', ';' })
        {
            var index = text.IndexOf(separator);
            if (index > 0)
            {
                email = text[..index].Trim();
                password = text[(index + 1)..].Trim();
                if (password.Length == 0)
                {
                    password = null;
                }

                break;
            }
        }

        if (!EmailRegex.IsMatch(email))
        {
            throw new FormatException($"Invalid email line: {line}");
        }

        return new EmailCredential { Email = email, Password = password };
    }

    public static IReadOnlyList<EmailCredential> Load(string path)
    {
        var emails = new List<EmailCredential>();
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

            if (!seen.Add(item.Email))
            {
                throw new FormatException($"{path}:{i + 1}: duplicate email {item.Email}");
            }

            emails.Add(item);
        }

        return emails;
    }

    public static IReadOnlyList<EmailCredential> Take(IReadOnlyList<EmailCredential> pool, int count)
    {
        if (count > pool.Count)
        {
            throw new InvalidOperationException($"Need {count} emails, but the pool only has {pool.Count}.");
        }

        return pool.Take(count).ToList();
    }
}
