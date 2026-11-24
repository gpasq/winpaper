# Builds the Windows ARM64 installer: build\Paperboy-<version>-arm64-setup.exe
# Bundles the .NET runtime (self-contained), so target PCs don't need .NET installed.
# Requires the .NET SDK and Inno Setup 6.3+ (scoop install extras/inno-setup).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = ([xml](Get-Content Paperboy.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$payload = 'build\installer-payload'
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue

dotnet publish Paperboy.csproj -c Release -r win-arm64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
              "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Inno Setup (ISCC.exe) not found. Install it with: scoop install extras/inno-setup' }

& $iscc /Q "/DAppVersion=$version" "/DPayloadDir=$PSScriptRoot\$payload" installer\Paperboy.iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
"Built build\Paperboy-$version-arm64-setup.exe"
