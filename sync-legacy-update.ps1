param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
# Old PlayWin clients validate both the repository URL and the PlayWin asset name.
# Keep a byte-identical copy of the latest YolanLive installer at that legacy endpoint.
function Invoke-GhJson([string[]]$Arguments) {
    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub command failed: $Arguments" }
    return ($output -join "`n" | ConvertFrom-Json)
}
$current = Invoke-GhJson @('api', 'repos/ShenShenSshen/YolanLive/releases/latest')
$tag = $current.tag_name
if ($tag -notmatch '^v\d+\.\d+\.\d+$' -or $current.draft -or $current.prerelease) { throw 'Expected a stable semantic release.' }
$version = $tag.Substring(1)
$name = "YolanLive-$version-x64-Setup.exe"
$legacyName = "PlayWin-$version-x64-Setup.exe"
$asset = @($current.assets | Where-Object name -eq $name)
if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:[a-fA-F0-9]{64}$') { throw 'Latest installer or SHA-256 is missing.' }
$asset = $asset[0]
function Assert-LegacyLatest {
    $legacy = Invoke-GhJson @('api', 'repos/ShenShenSshen/PlayWin/releases/latest')
    if ($legacy.tag_name -ne $tag) { throw "Legacy updater offers $($legacy.tag_name), latest is $tag." }
    $copy = @($legacy.assets | Where-Object name -eq $legacyName)
    $expectedUrl = "https://github.com/ShenShenSshen/PlayWin/releases/download/$tag/$legacyName"
    if ($copy.Count -ne 1 -or $copy[0].browser_download_url -ne $expectedUrl -or
        $copy[0].digest -ne $asset.digest -or $copy[0].size -ne $asset.size) {
        throw 'Legacy updater installer name, URL, size or digest does not match the current release.'
    }
    "PASS old PlayWin clients can update directly to $tag; installer SHA-256 matches YolanLive."
}
if ($CheckOnly) { Assert-LegacyLatest; return }
$staging = Join-Path $PSScriptRoot "artifacts\legacy-update\$tag"
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$source = Join-Path $PSScriptRoot "artifacts\$name"
if (-not (Test-Path -LiteralPath $source)) { throw 'Download the published installer to artifacts before synchronizing.' }
if ((Get-Item -LiteralPath $source).Length -ne $asset.size -or
    ('sha256:' + (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()) -ne $asset.digest) {
    throw 'Local installer differs from the published installer; refusing to replace a published release.'
}
$existing = @(Invoke-GhJson @('api', 'repos/ShenShenSshen/PlayWin/releases?per_page=100') | Where-Object tag_name -eq $tag)
if ($existing.Count -gt 0 -and -not $existing[0].draft) { Assert-LegacyLatest; return }
$copyPath = Join-Path $staging $legacyName
Copy-Item -LiteralPath $source -Destination $copyPath -Force
$sums = Join-Path $staging 'SHA256SUMS.txt'
Set-Content -LiteralPath $sums -Encoding ascii -Value ($asset.digest.Substring(7) + '  ' + $legacyName)
$notes = Join-Path $staging 'notes.md'
@"
旧版 PlayWin 一次升级到游岚 YolanLive $tag，无需逐版本安装。

兼容安装包仅使用旧文件名，内容与 [YolanLive $tag]($($current.html_url)) 的正式安装包完全一致，SHA-256 相同。覆盖升级保留资料；安装后自动使用 YolanLive 更新源。

更新内容见上述正式发布页。本仓库仅保留旧客户端更新入口。
"@ | Set-Content -LiteralPath $notes -Encoding utf8
if ($existing.Count -eq 0) {
    & gh release create $tag --repo ShenShenSshen/PlayWin --draft --title "YolanLive $tag - 旧版直接升级入口" --notes-file $notes
    if ($LASTEXITCODE -ne 0) { throw 'Could not create legacy draft.' }
}
$draftAssets = @($existing | ForEach-Object { $_.assets } | Where-Object name -eq $legacyName)
if ($draftAssets.Count -ne 1 -or $draftAssets[0].digest -ne $asset.digest -or $draftAssets[0].size -ne $asset.size) {
    & gh release upload $tag $copyPath $sums --repo ShenShenSshen/PlayWin --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Legacy draft upload failed.' }
}
# Draft releases may not yet have a Git tag, so find the draft through the release list.
$uploaded = @(Invoke-GhJson @('api', 'repos/ShenShenSshen/PlayWin/releases?per_page=100') | Where-Object tag_name -eq $tag)
if ($uploaded.Count -ne 1) { throw 'Expected exactly one legacy release.' }
$uploaded = $uploaded[0]
$remote = @($uploaded.assets | Where-Object name -eq $legacyName)
if ($remote.Count -ne 1 -or $remote[0].digest -ne $asset.digest -or $remote[0].size -ne $asset.size) { throw 'Uploaded legacy installer verification failed; draft kept unpublished.' }
& gh release edit $tag --repo ShenShenSshen/PlayWin --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Publishing legacy entry failed.' }
Assert-LegacyLatest
