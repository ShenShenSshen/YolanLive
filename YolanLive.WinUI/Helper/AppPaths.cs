using System;
using System.IO;
using System.Linq;
namespace YolanLive.WinUI.Helper;
public static class AppPaths
{
    public static string DataDirectory { get; } = GetDataDirectory();
    // Keep the original storage identity so upgrades preserve settings, database and encrypted browser sessions.
    private static string GetDataDirectory()
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Any(a => a == "--smoke-test" || a == "--category-repro"))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayWin");
        var testRoot = Path.Combine(Path.GetTempPath(), "YolanLive-smoke-test");
        var inherited = Environment.GetEnvironmentVariable("YOLANLIVE_SMOKE_PROFILE");
        if (args.Contains("--smoke-test") && !string.IsNullOrEmpty(inherited))
        {
            var resolved = Path.GetFullPath(inherited);
            if (resolved.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) return resolved;
            throw new ArgumentException("Smoke profile must be inside the isolated test directory.");
        }
        return Path.Combine(testRoot, Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
    }
    public static string EnsureDirectory() => Directory.CreateDirectory(DataDirectory).FullName;
}
