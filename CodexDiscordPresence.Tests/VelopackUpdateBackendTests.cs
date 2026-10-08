using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace CodexDiscordPresence.Tests;

public sealed class VelopackUpdateBackendTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealSdk_DownloadsVerifiedPackageAndRejectsDamagedPackage(bool damagePackage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "rpc-velopack-tests-" + Guid.NewGuid());
        var feedDirectory = Path.Combine(directory, "feed");
        var packagesDirectory = Path.Combine(directory, "packages");
        Directory.CreateDirectory(feedDirectory);
        Directory.CreateDirectory(packagesDirectory);
        try
        {
            var id = "UpdateTest" + Guid.NewGuid().ToString("N");
            var fileName = id + "-0.5.0-full.nupkg";
            var packagePath = Path.Combine(feedDirectory, fileName);
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry(id + ".nuspec").Open());
                writer.Write($"<package><metadata><id>{id}</id><version>0.5.0</version><authors>Test</authors><description>Test</description></metadata></package>");
            }
            var bytes = File.ReadAllBytes(packagePath);
            var feed = new { Assets = new[] { new { PackageId = id, Version = "0.5.0", Type = "Full", FileName = fileName,
                SHA1 = Convert.ToHexString(SHA1.HashData(bytes)), SHA256 = Convert.ToHexString(SHA256.HashData(bytes)), Size = bytes.Length } } };
            File.WriteAllText(Path.Combine(feedDirectory, "releases.win.json"), JsonSerializer.Serialize(feed));
            if (damagePackage)
            {
                bytes[^1] ^= 0xff;
                File.WriteAllBytes(packagePath, bytes); // Same length; checksum, not just size, must detect this.
            }
            var locator = new TestVelopackLocator(id, "0.2.5", packagesDirectory);
            using var backend = new VelopackUpdateBackend(new UpdateManager(new SimpleFileSource(new DirectoryInfo(feedDirectory)),
                new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false }, locator));
            Assert.True(backend.IsInstalled);
            Assert.Equal("0.5.0", await backend.CheckAsync(default));
            if (damagePackage)
            {
                await Assert.ThrowsAnyAsync<Exception>(() => backend.DownloadAsync(_ => { }, default));
                Assert.Null(backend.PreparedVersion);
            }
            else
            {
                await backend.DownloadAsync(_ => { }, default);
                Assert.Equal("0.5.0", backend.PreparedVersion);
                Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(packagesDirectory, fileName)));
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
