param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
if (!$IsWindows -or !$env:GITHUB_ACTIONS) { throw '安装集成测试需在 GitHub Actions 的临时 Windows 用户环境中运行。' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $projectRoot ('artifacts/installer-tests/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$installDirectory = Join-Path $testRoot 'ApiNet installation 测试'
$dataDirectory = Join-Path $testRoot 'ApiNet data'
$previousDataDirectory = $env:APINET_DATA_DIR
$env:APINET_DATA_DIR = $dataDirectory

function Invoke-Setup([string]$File, [string[]]$SetupArguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    foreach ($argument in $SetupArguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (!$process.WaitForExit(90000)) { $process.Kill($true); throw '安装超时，可能出现了阻塞安装的确认对话框。' }
        if ($process.ExitCode -ne 0) { throw "安装进程失败：$($process.ExitCode)" }
    } finally { $process.Dispose() }
}

function Assert-ApplicationIcon([string]$File) {
    $actual = [Drawing.Icon]::ExtractAssociatedIcon($File)
    $expected = [Drawing.Icon]::new((Join-Path $projectRoot 'src/ApiNet.UI/Assets/app.ico'), $actual.Size)
    $actualImage = $actual.ToBitmap()
    $expectedImage = $expected.ToBitmap()
    try {
        for ($y = 0; $y -lt $actualImage.Height; $y++) {
            for ($x = 0; $x -lt $actualImage.Width; $x++) {
                if ($actualImage.GetPixel($x, $y).ToArgb() -ne $expectedImage.GetPixel($x, $y).ToArgb()) { throw "应用图标与网络 Logo 不一致：$File" }
            }
        }
    } finally { $actualImage.Dispose(); $expectedImage.Dispose(); $actual.Dispose(); $expected.Dispose() }
}

try {
    $legacy = Join-Path $testRoot 'ApiNet-0.1.0-Setup.exe'
    Invoke-WebRequest -Uri 'https://github.com/coolqingcheng/ApiNet/releases/download/v0.1.0/ApiNet-win-x64-Setup.exe' -OutFile $legacy
    Invoke-Setup $legacy @('--silent','--installto',$installDirectory,'--log',(Join-Path $testRoot 'legacy.log'))
    # Opaque fixtures verify that the installer preserves every byte, including a
    # pending WAL. Service tests separately verify a real SQLite database migration.
    [IO.File]::WriteAllText((Join-Path $installDirectory 'workspace.db'), 'workspace-and-history-fixture')
    [IO.File]::WriteAllText((Join-Path $installDirectory 'workspace.db-wal'), 'pending-wal-fixture')
    New-Item -ItemType Directory -Path (Join-Path $installDirectory 'certificates') -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $installDirectory 'certificates/apinet-root.pfx'), [byte[]](0,1,255,128))
    $setup = (Get-ChildItem (Join-Path $projectRoot 'artifacts/releases/win-x64') -Filter '*Setup.exe' | Select-Object -First 1).FullName
    if (!$setup) { throw '未找到待验证的安装包。' }
    $upgradeLog = Join-Path $testRoot 'upgrade.log'
    # No --silent on the public installer: normal double-click behavior must accept repair.
    Invoke-Setup $setup @('--no-start','--installto',$installDirectory,'--log',$upgradeLog)
    if ([IO.File]::ReadAllText((Join-Path $dataDirectory 'workspace.db')) -ne 'workspace-and-history-fixture') { throw '旧工作区未保留。' }
    if ([IO.File]::ReadAllText((Join-Path $dataDirectory 'workspace.db-wal')) -ne 'pending-wal-fixture') { throw 'WAL 未保留。' }
    if ([Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $dataDirectory 'certificates/apinet-root.pfx'))) -ne '0001FF80') { throw '本地 CA 未保留。' }
    if ((Get-Content $upgradeLog -Raw) -notmatch 'User chose to overwrite existing installation') { throw '升级未执行覆盖安装。' }
    [IO.File]::WriteAllText((Join-Path $dataDirectory 'workspace.db'), 'data-changed-after-upgrade')
    $library = Join-Path $installDirectory 'current/libSkiaSharp.dll'
    Remove-Item -LiteralPath $library
    $repairLog = Join-Path $testRoot 'repair.log'
    Invoke-Setup $setup @('--no-start','--installto',$installDirectory,'--log',$repairLog)
    if (!(Test-Path -LiteralPath $library)) { throw '同版本修复没有恢复缺失文件。' }
    if ([IO.File]::ReadAllText((Join-Path $dataDirectory 'workspace.db')) -ne 'data-changed-after-upgrade') { throw '修复覆盖了用户数据。' }
    $manifest = [xml](Get-Content (Join-Path $installDirectory 'current/sq.version') -Raw)
    if ($manifest.package.metadata.version -ne $Version) { throw '安装版本不匹配。' }
    if ((Get-Content $repairLog -Raw) -notmatch 'User chose to overwrite existing installation') { throw '同版本安装未进入修复流程。' }
    Assert-ApplicationIcon $setup
    Assert-ApplicationIcon (Join-Path $installDirectory 'ApiNet.exe')
    Assert-ApplicationIcon (Join-Path $installDirectory 'current/ApiNet.Desktop.exe')
    Write-Host "已验证：0.1.0 → $Version 覆盖安装、同版本修复、缺失文件恢复、数据库/WAL/CA 保留。"
    Write-Host '已验证：安装包、启动器和 Native AOT 主程序均嵌入相同的网络 Logo。'
} finally {
    $env:APINET_DATA_DIR = $previousDataDirectory
    Get-ChildItem $testRoot -Filter '*.log' | ForEach-Object { Write-Host "安装日志：$($_.FullName)"; Get-Content $_.FullName -Tail 8 }
    # The hosted runner is discarded after the job, retaining these files for diagnosis.
}
