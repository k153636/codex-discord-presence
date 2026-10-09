namespace CodexDiscordPresence;

internal static class BundledAppContent
{
    private const string Prefix = "Bundled/";
    private static readonly Lazy<string> ContentDirectory = new(() => ExtractTo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexDiscordPresence", "bundled-content", typeof(BundledAppContent).Module.ModuleVersionId.ToString("N"))));

    internal static string AssetsDirectory => Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Dashboard"))
        ? Path.Combine(AppContext.BaseDirectory, "Assets")
        : Path.Combine(ContentDirectory.Value, "Assets");

    internal static string DefaultSettingsPath(string fileName) => Path.Combine(ContentDirectory.Value, fileName);

    internal static string ExtractTo(string directory)
    {
        var assembly = typeof(BundledAppContent).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            var relative = name[Prefix.Length..].Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Atomic replacement keeps concurrent hook/tray processes from reading partial content.
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var source = assembly.GetManifestResourceStream(name)!)
                using (var target = File.Create(temporary)) source.CopyTo(target);
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return directory;
    }
}
