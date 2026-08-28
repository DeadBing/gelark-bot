namespace GelarkBot;

public static class ProfilePlanner
{
    public static IReadOnlyList<ProfilePlan> Build(
        IReadOnlyList<ProxyEndpoint> proxies,
        IReadOnlyList<EmailCredential>? emails,
        string namePrefix,
        string? group,
        bool nameFromEmail)
    {
        if (emails is { Count: > 0 } && emails.Count < proxies.Count)
        {
            throw new InvalidOperationException($"Need {proxies.Count} accounts, but the pool only has {emails.Count}.");
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<ProfilePlan>(proxies.Count);
        for (var i = 0; i < proxies.Count; i++)
        {
            var email = emails is { Count: > 0 } ? emails[i] : null;
            var name = NextName(namePrefix, i + 1, email, nameFromEmail, usedNames);
            var tags = new List<string> { "floppydata" };
            if (!string.IsNullOrWhiteSpace(proxies[i].Country))
            {
                tags.Add(proxies[i].Country!);
            }

            plans.Add(new ProfilePlan
            {
                ProfileName = name,
                Proxy = proxies[i],
                Email = email,
                ProfileNote = email?.NoteLine() ?? "",
                ProfileTags = tags,
                ProfileGroup = group,
            });
        }

        return plans;
    }

    private static string NextName(
        string prefix,
        int index,
        EmailCredential? email,
        bool nameFromEmail,
        HashSet<string> usedNames)
    {
        string candidate;
        if (nameFromEmail && email is not null)
        {
            var local = email.Login.Contains('@') ? email.Login.Split('@')[0] : email.Login;
            candidate = NameUtil.Sanitize(local);
            var suffix = 2;
            var original = candidate;
            while (!usedNames.Add(candidate))
            {
                candidate = NameUtil.Sanitize($"{original}-{suffix}");
                suffix++;
            }

            return candidate;
        }

        candidate = NameUtil.Sanitize($"{prefix}-{index:000}");
        var extra = 2;
        var baseName = candidate;
        while (!usedNames.Add(candidate))
        {
            candidate = NameUtil.Sanitize($"{baseName}-{extra}");
            extra++;
        }

        return candidate;
    }
}
