namespace GelarkBot.Tests;

public class DotEnvTests
{
    [Fact]
    public void Load_SetsMissingVariablesOnly()
    {
        var key = $"GELARK_TEST_{Guid.NewGuid():N}";
        var existing = $"GELARK_EXISTING_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(existing, "keep");
        var path = Path.Combine(Path.GetTempPath(), $"{key}.env");
        File.WriteAllText(path, $"{key}=from-file\n{existing}=overwrite\n# comment\n");
        try
        {
            DotEnv.Load(path);
            Assert.Equal("from-file", Environment.GetEnvironmentVariable(key));
            Assert.Equal("keep", Environment.GetEnvironmentVariable(existing));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
            Environment.SetEnvironmentVariable(existing, null);
            File.Delete(path);
        }
    }
}
