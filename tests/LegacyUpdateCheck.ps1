param([switch]$Live)
$checkRoot = $PSScriptRoot
& (Join-Path $checkRoot '..\with-build-cache.ps1') {
    $ErrorActionPreference = 'Stop'
    $root = [IO.Path]::GetFullPath((Join-Path $checkRoot '..'))
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'YolanLive.WinUI\YolanLive.csproj') -Raw
    $targetVersion = [string]$project.Project.PropertyGroup.Version
    $fixture = Join-Path $root 'artifacts\legacy-update-check'
    New-Item -ItemType Directory -Path $fixture -Force | Out-Null
    # Compile the exact published updater rather than the renamed implementation.
    $old = & git -c ('safe.directory=' + $root.Replace('\','/')) -C $root show 'v1.0.6:PlayWin.WinUI/Helper/UpdateClient.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read immutable v1.0.6 update implementation.' }
    Set-Content -LiteralPath (Join-Path $fixture 'LegacyUpdateClient.cs') -Value $old -Encoding UTF8
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><AssemblyVersion>1.0.6.0</AssemblyVersion><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup><PackageReference Include="Newtonsoft.Json" Version="13.0.3" /></ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $fixture 'LegacyUpdateCheck.csproj') -Encoding UTF8
    @'
using Newtonsoft.Json.Linq;
using PlayWin.WinUI.Helper;
using System.Security.Cryptography;
var version = Environment.GetEnvironmentVariable("YOLANLIVE_LEGACY_TARGET") ?? throw new Exception("Target version required");
var tag = "v" + version;
var name = "PlayWin-" + version + "-x64-Setup.exe";
var asset = new JObject { ["name"] = name, ["browser_download_url"] = UpdateClient.Repository + "/releases/download/" + tag + "/" + name, ["size"] = 1, ["digest"] = "sha256:" + new string('a', 64) };
var release = new JObject { ["tag_name"] = tag, ["draft"] = false, ["prerelease"] = false, ["assets"] = new JArray(asset) };
if (UpdateClient.ParseRelease(release.ToString()).Version <= UpdateClient.ParseVersion("v1.0.6")) throw new Exception("Target version does not upgrade old client.");
Console.WriteLine("PASS immutable v1.0.6 client accepts direct latest version metadata: " + tag);
asset["browser_download_url"] = "https://github.com/ShenShenSshen/YolanLive/releases/download/" + tag + "/" + name;
try { UpdateClient.ParseRelease(release.ToString()); throw new Exception("Unexpected redirected URL accepted"); }
catch (InvalidDataException) { Console.WriteLine("PASS immutable old client rejects redirected asset URL; bridge repository is necessary"); }
if (args.Contains("--live")) {
    var client = new UpdateClient(directory: Path.Combine(AppPaths.DataDirectory, "download"));
    var actual = await client.CheckAsync(CancellationToken.None);
    if (actual.Tag != tag) throw new Exception("Old repository does not offer " + tag);
    var path = await client.DownloadAsync(actual, _ => {}, CancellationToken.None);
    if (!await client.VerifyAsync(actual, path, CancellationToken.None)) throw new Exception("Old updater download failed verification.");
    var expected = Path.Combine(AppPaths.DataDirectory, "..", "YolanLive-" + version + "-x64-Setup.exe");
    if (!File.Exists(expected) || !SHA256.HashData(File.ReadAllBytes(expected)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))) throw new Exception("Bridge installer differs from formal YolanLive installer.");
    Console.WriteLine("PASS immutable v1.0.6 updater downloads byte-identical latest YolanLive installer: " + tag);
    Console.WriteLine("Installer executed=False; account data accessed=False");
}
namespace PlayWin.WinUI { public sealed class App {} }
namespace PlayWin.WinUI.Helper { public static class AppPaths { public static string DataDirectory => Environment.GetEnvironmentVariable("YOLANLIVE_LEGACY_FIXTURE") ?? throw new Exception("Isolated directory required"); } }
'@ | Set-Content -LiteralPath (Join-Path $fixture 'Program.cs') -Encoding UTF8
    $previous = $env:YOLANLIVE_LEGACY_FIXTURE
    $previousTarget = $env:YOLANLIVE_LEGACY_TARGET
    try {
        $env:YOLANLIVE_LEGACY_FIXTURE = $fixture
        $env:YOLANLIVE_LEGACY_TARGET = $targetVersion
        $argsList = @('run','--project',(Join-Path $fixture 'LegacyUpdateCheck.csproj'),'-c','Release','--')
        if ($Live) { $argsList += '--live' }
        & dotnet @argsList
        if ($LASTEXITCODE -ne 0) { throw 'Legacy updater compatibility check failed.' }
    } finally { $env:YOLANLIVE_LEGACY_FIXTURE = $previous; $env:YOLANLIVE_LEGACY_TARGET = $previousTarget }
}
