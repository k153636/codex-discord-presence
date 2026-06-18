using System.Reflection;

namespace CodexDiscordPresence;

public static class AppVersion
{
    public static SemanticVersion Current { get; } = ParseCurrent();

    private static SemanticVersion ParseCurrent()
    {
        var assembly = typeof(AppVersion).Assembly;
        var metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "AppVersion", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (SemanticVersion.TryParse(metadata, out var version))
        {
            return version!;
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is not null)
        {
            var fallback = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(0, assemblyVersion.Build)}";
            if (SemanticVersion.TryParse(fallback, out var fallbackVersion) && fallbackVersion is not null)
            {
                return fallbackVersion;
            }
        }

        throw new InvalidOperationException("The application version metadata was not found or was invalid.");
    }
}
