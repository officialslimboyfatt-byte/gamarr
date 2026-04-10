namespace Gamarr.Agent.Services;

internal static class AgentPathResolver
{
    public static string GetWritableGamarrRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Gamarr"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gamarr"),
            Path.Combine(Path.GetTempPath(), "Gamarr")
        };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probePath = Path.Combine(candidate, $".write-test-{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probePath, "ok");
                File.Delete(probePath);
                return candidate;
            }
            catch
            {
                // Try the next writable location.
            }
        }

        throw new InvalidOperationException("Unable to resolve a writable Gamarr state directory.");
    }
}
