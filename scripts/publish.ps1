param(
    [Parameter(Mandatory)][ValidateSet('win-x64','linux-x64','osx-arm64')][string]$Rid,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')][string]$Version = '0.1.20',
    [switch]$PublishOnly
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $targetOs = $Rid.Split('-')[0]
    if (($targetOs -eq 'win' -and !$IsWindows) -or ($targetOs -eq 'linux' -and !$IsLinux) -or ($targetOs -eq 'osx' -and !$IsMacOS)) {
        throw 'Native AOT 需要在目标操作系统上运行此脚本；同一系统内可使用对应工具链构建另一种架构。'
    }
    $publishDirectory = Join-Path $projectRoot "artifacts/publish/$Rid"
    $releaseDirectory = Join-Path $projectRoot "artifacts/releases/$Rid"
    dotnet publish src/PostNet.Desktop/PostNet.Desktop.csproj -c Release -r $Rid -p:Version=$Version -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Native AOT 发布失败。' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDirectory
    if ($PublishOnly) { return }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Velopack 工具恢复失败。' }
    $mainExe = if ($IsWindows) { 'ApiNet.Desktop.exe' } else { 'ApiNet.Desktop' }
    $icon = Join-Path $projectRoot 'src/PostNet.UI/Assets/app.png'
    if ($IsWindows) { $icon = Join-Path $projectRoot 'src/PostNet.UI/Assets/app.ico' }
    if ($IsMacOS) {
        $iconSet = Join-Path $projectRoot 'artifacts/PostNet.iconset'
        New-Item -ItemType Directory -Path $iconSet -Force | Out-Null
        foreach ($size in 16,32,128,256,512) {
            sips -z $size $size $icon --out (Join-Path $iconSet "icon_${size}x${size}.png") | Out-Null
            $double = $size * 2
            sips -z $double $double $icon --out (Join-Path $iconSet "icon_${size}x${size}@2x.png") | Out-Null
        }
        $icon = Join-Path $projectRoot 'artifacts/PostNet.icns'
        iconutil -c icns $iconSet -o $icon
        if ($LASTEXITCODE -ne 0) { throw 'macOS 图标生成失败。' }
    }
    $releaseNotes = Join-Path $projectRoot 'docs/RELEASE-NOTES.md'
    # The package ID is persisted by Velopack; keep it stable across the product rename.
    $packArguments = @('vpk','pack','--packId','ApiNet','--packTitle','Post.NET','--packVersion',$Version,'--packDir',$publishDirectory,'--mainExe',$mainExe,'--runtime',$Rid,'--channel',$Rid,'--icon',$icon,'--releaseNotes',$releaseNotes,'--outputDir',$releaseDirectory)
    # Windows Native AOT 无托管入口可供扫描；此参数仅 Windows 支持。
    # Program.Main 已显式调用 VelopackApp.Run。
    if ($IsWindows) { $packArguments += '--skipVeloAppCheck' }
    if ($IsMacOS) { $packArguments += '--noInst' }
    & dotnet @packArguments
    if ($LASTEXITCODE -ne 0) { throw 'Velopack 打包失败。' }
    $feed = Get-Content -LiteralPath (Join-Path $releaseDirectory "releases.$Rid.json") -Raw | ConvertFrom-Json
    $fullRelease = @($feed.Assets | Where-Object { $_.Type -eq 'Full' -and $_.Version -eq $Version })
    if ($fullRelease.Count -ne 1) { throw '更新索引中未找到唯一的目标版本完整包。' }
    $expectedNotes = [IO.File]::ReadAllText($releaseNotes).Replace("`r`n", "`n").Trim()
    $actualNotes = ([string]$fullRelease[0].NotesMarkdown).Replace("`r`n", "`n").Trim()
    if (!$expectedNotes -or $actualNotes -cne $expectedNotes) { throw '更新索引中的更新说明与发布说明不一致。' }
    if ($IsWindows) {
        $nativeInstaller = Get-ChildItem -LiteralPath $releaseDirectory -Filter '*Setup.exe' | Select-Object -First 1
        if (!$nativeInstaller) { throw '未找到 Velopack Windows 安装包。' }
        $installerDirectory = Join-Path $projectRoot "artifacts/installer/$Rid"
        dotnet publish src/PostNet.Setup/PostNet.Setup.csproj -c Release -r $Rid -p:Version=$Version "-p:InstallerPayload=$($nativeInstaller.FullName)" -o $installerDirectory
        if ($LASTEXITCODE -ne 0) { throw '覆盖安装器构建失败。' }
        Copy-Item -LiteralPath (Join-Path $installerDirectory 'PostNet.Setup.exe') -Destination $nativeInstaller.FullName -Force
    }
    if ($IsMacOS) {
        $portable = Get-ChildItem -LiteralPath $releaseDirectory -Filter '*Portable*.zip' | Select-Object -First 1
        if (!$portable) { throw '未找到 macOS 便携应用包。' }
        $stageDirectory = Join-Path $projectRoot "artifacts/dmg-$Rid-$Version"
        if (Test-Path -LiteralPath $stageDirectory) { throw "DMG 临时目录已存在，请使用新的版本号或清理 $stageDirectory" }
        New-Item -ItemType Directory -Path $stageDirectory | Out-Null
        ditto -x -k $portable.FullName $stageDirectory
        if ($LASTEXITCODE -ne 0) { throw '解压 macOS 应用包失败。' }
        hdiutil create -volname Post.NET -srcfolder $stageDirectory -format UDZO -ov (Join-Path $releaseDirectory "Post.NET-$Version-$Rid.dmg")
        if ($LASTEXITCODE -ne 0) { throw 'DMG 生成失败。' }
    }
    # Brand public downloads while leaving update package identities and feeds compatible.
    $renamedAssets = @{}
    foreach ($file in Get-ChildItem -LiteralPath $releaseDirectory -File) {
        if ($file.Name.StartsWith('ApiNet-') -and $file.Extension -in '.exe','.zip','.AppImage') {
            $name = 'Post.NET-' + $file.Name.Substring('ApiNet-'.Length)
            Rename-Item -LiteralPath $file.FullName -NewName $name
            $renamedAssets[$file.Name] = $name
        }
    }
    $assetManifest = Join-Path $releaseDirectory "assets.$Rid.json"
    $assets = @(Get-Content -LiteralPath $assetManifest -Raw | ConvertFrom-Json)
    foreach ($asset in $assets) {
        if ($renamedAssets.ContainsKey($asset.RelativeFileName)) { $asset.RelativeFileName = $renamedAssets[$asset.RelativeFileName] }
        if (!(Test-Path -LiteralPath (Join-Path $releaseDirectory $asset.RelativeFileName))) { throw "发布资产缺失：$($asset.RelativeFileName)" }
    }
    ConvertTo-Json -InputObject $assets -Depth 10 | Set-Content -LiteralPath $assetManifest -Encoding utf8
    Write-Host "已生成：$releaseDirectory"
}
finally { Pop-Location }
