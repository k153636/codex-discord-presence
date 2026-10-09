using System.Security.Cryptography;

namespace CodexDiscordPresence.Tests;

public sealed class BundledAppContentTests
{
    [Fact]
    public void ExtractTo_PreservesOriginalDashboardArtworkSettingsAndLicenses()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bundled-content-" + Guid.NewGuid().ToString("N"));
        try
        {
            var assembly = typeof(AppOptions).Assembly;
            BundledAppContent.ExtractTo(directory);
            var names = assembly.GetManifestResourceNames().Where(name => name.StartsWith("Bundled/", StringComparison.Ordinal)).ToArray();
            Assert.Contains(names, name => name.Replace('\\', '/') == "Bundled/Assets/Dashboard/Dashboard.html");
            Assert.Contains(names, name => name.Replace('\\', '/') == "Bundled/appsettings.json");
            Assert.Contains(names, name => name.Replace('\\', '/') == "Bundled/THIRD-PARTY-NOTICES.md");
            foreach (var name in names)
            {
                using var resource = assembly.GetManifestResourceStream(name)!;
                var path = Path.Combine(directory, name[8..].Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(SHA256.HashData(resource), SHA256.HashData(File.ReadAllBytes(path)));
            }
            var dashboard = Path.Combine(directory, "Assets", "Dashboard", "Dashboard.html");
            File.WriteAllText(dashboard, "incomplete");
            BundledAppContent.ExtractTo(directory);
            Assert.NotEqual("incomplete", File.ReadAllText(dashboard));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories));
            Assert.False(string.IsNullOrWhiteSpace(AppOptions.LoadFromFile(Path.Combine(directory, "appsettings.json")).Discord.ClientId));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
