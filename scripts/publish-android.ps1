param(
    [ValidateSet('android-arm64','android-x64')][string]$Rid = 'android-arm64',
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')][string]$Version = '0.1.4',
    [Parameter(Mandatory)][string]$KeyStore
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$versionParts = $Version.Split('.') | ForEach-Object { [int]$_ }
$versionCode = $versionParts[0] * 1000000 + $versionParts[1] * 10000 + $versionParts[2] * 100
if ($versionCode -lt 1 -or $versionCode -gt 2100000000) { throw 'Android version code is outside the supported range.' }
if (!$env:APINET_SIGN_PASSWORD) { throw 'Set APINET_SIGN_PASSWORD to the private keystore password.' }
$androidOutput = Join-Path $projectRoot "artifacts/android/$Rid"
$androidRelease = Join-Path $projectRoot "artifacts/releases/$Rid"
New-Item -ItemType Directory -Path $androidOutput,$androidRelease -Force | Out-Null
dotnet publish (Join-Path $projectRoot 'src/ApiNet.Android/ApiNet.Android.csproj') -c Release -r $Rid -o $androidOutput `
    -p:Version=$Version -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=$versionCode `
    -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=$KeyStore -p:AndroidSigningKeyAlias=apinet `
    -p:AndroidSigningStorePass=env:APINET_SIGN_PASSWORD -p:AndroidSigningKeyPass=env:APINET_SIGN_PASSWORD
if ($LASTEXITCODE -ne 0) { throw 'Android publish failed.' }
$signedApk = Get-ChildItem $androidOutput -Filter '*-Signed.apk' | Select-Object -First 1
if (!$signedApk) { throw 'The signed APK was not generated.' }
Copy-Item -LiteralPath $signedApk.FullName -Destination (Join-Path $androidRelease "ApiNet-$Rid.apk")
