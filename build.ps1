# Builds Paperboy.exe into .\build. Pass -Install to copy it to %LOCALAPPDATA%\Programs\Paperboy and launch it.
param([switch]$Install)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
dotnet publish Paperboy.csproj -c Release -r $rid --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Remove-Item build\*.pdb -ErrorAction SilentlyContinue
"Built build\Paperboy.exe"

if ($Install) {
    $dest = Join-Path $env:LOCALAPPDATA 'Programs\Paperboy'
    Get-Process Paperboy -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item build\Paperboy.exe $dest -Force
    Start-Process (Join-Path $dest 'Paperboy.exe')
    "Installed to $dest\Paperboy.exe"
}
