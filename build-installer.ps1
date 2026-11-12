# ==============================================================================
# River — Automated All-in-One Installer Build Script
# Generates self-contained River-Setup-x64.exe and deploys to Desktop
# ==============================================================================

[CmdletBinding()]
param(
    [switch]$SkipAppPublish = $false
)

$ErrorActionPreference = 'Stop'
$sw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host '======================================================================' -ForegroundColor Cyan
Write-Host ' River — Building Premium All-in-One Installer (River-Setup-x64.exe)' -ForegroundColor Cyan
Write-Host '======================================================================' -ForegroundColor Cyan

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = (Get-Location).Path
}

$appProject = Join-Path $scriptRoot 'src\MovieTime.App\MovieTime.App.csproj'
$installerProject = Join-Path $scriptRoot 'src\MovieTime.Installer\MovieTime.Installer.csproj'
$appPublishDir = Join-Path $scriptRoot 'bin\publish_app'
$installerPublishDir = Join-Path $scriptRoot 'bin\publish_installer'
$payloadZip = Join-Path $scriptRoot 'src\MovieTime.Installer\RiverPayload.zip'

$desktopDir = Join-Path $env:USERPROFILE 'Desktop\River Installer'
$finalInstallerPath = Join-Path $desktopDir 'River-Setup-x64.exe'

# 1. Publish River App (Self-Contained win-x64)
if (-not $SkipAppPublish -or -not (Test-Path $appPublishDir)) {
    Write-Host ''
    Write-Host '[Step 1/5] Publishing River App (Self-Contained win-x64 Release)...' -ForegroundColor Yellow
    if (Test-Path $appPublishDir) {
        Remove-Item -Recurse -Force $appPublishDir -ErrorAction SilentlyContinue
    }

    & dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $appPublishDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error 'Failed to publish MovieTime.App'
        exit 1
    }
} else {
    Write-Host ''
    Write-Host "[Step 1/5] Skipping River App publish (using existing $appPublishDir)..." -ForegroundColor Yellow
}

# 2. Package App Payload into RiverPayload.zip
Write-Host ''
Write-Host '[Step 2/5] Compressing payload archive into RiverPayload.zip...' -ForegroundColor Yellow
if (Test-Path $payloadZip) {
    Remove-Item -Force $payloadZip -ErrorAction SilentlyContinue
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $appPublishDir,
    $payloadZip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false
)

$payloadItem = Get-Item $payloadZip
$zipSizeMb = [math]::Round(($payloadItem.Length / 1048576), 2)
Write-Host "Payload archive created: $payloadZip" -ForegroundColor Green
Write-Host "Payload size: $zipSizeMb MB" -ForegroundColor Green

# 3. Publish MovieTime.Installer as a Single-File Self-Contained Executable
Write-Host ''
Write-Host '[Step 3/5] Publishing River-Setup-x64.exe (Single-File Self-Contained)...' -ForegroundColor Yellow
if (Test-Path $installerPublishDir) {
    Remove-Item -Recurse -Force $installerPublishDir -ErrorAction SilentlyContinue
}

& dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $installerPublishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Failed to publish MovieTime.Installer'
    exit 1
}

$generatedExe = Join-Path $installerPublishDir 'River-Setup-x64.exe'
if (-not (Test-Path $generatedExe)) {
    Write-Error "Expected installer binary not found at $generatedExe"
    exit 1
}

$exeItem = Get-Item $generatedExe
$exeSizeMb = [math]::Round(($exeItem.Length / 1048576), 2)
Write-Host "Installer binary compiled successfully: $generatedExe" -ForegroundColor Green
Write-Host "Installer size: $exeSizeMb MB" -ForegroundColor Green

# 4. Deploy Final Installer to User Desktop
Write-Host ''
Write-Host '[Step 4/5] Deploying final installer to Desktop...' -ForegroundColor Yellow
if (-not (Test-Path $desktopDir)) {
    New-Item -ItemType Directory -Path $desktopDir -Force | Out-Null
    Write-Host "Created output directory: $desktopDir" -ForegroundColor Green
}

Copy-Item -Path $generatedExe -Destination $finalInstallerPath -Force
Write-Host "Copied to: $finalInstallerPath" -ForegroundColor Green

# 5. Verify Output and Compute Checksums
Write-Host ''
Write-Host '[Step 5/5] Verifying final output file and integrity...' -ForegroundColor Yellow
if (-not (Test-Path $finalInstallerPath)) {
    Write-Error "Validation failed: Final installer file does not exist at $finalInstallerPath"
    exit 1
}

$destItem = Get-Item $finalInstallerPath
if ($destItem.Length -le 0) {
    Write-Error 'Validation failed: Final installer file size is 0 bytes.'
    exit 1
}

$sourceHash = (Get-FileHash -Path $generatedExe -Algorithm SHA256).Hash
$destHash = (Get-FileHash -Path $finalInstallerPath -Algorithm SHA256).Hash

if ($sourceHash -ne $destHash) {
    Write-Error 'Validation failed: SHA256 checksum mismatch between build output and Desktop deploy.'
    exit 1
}

$sw.Stop()

$destSizeMb = [math]::Round(($destItem.Length / 1048576), 2)
$elapsedSec = [math]::Round($sw.Elapsed.TotalSeconds, 1)

Write-Host ''
Write-Host '======================================================================' -ForegroundColor Green
Write-Host ' RIVER INSTALLER BUILD COMPLETE!' -ForegroundColor Green
Write-Host '======================================================================' -ForegroundColor Green
Write-Host "Final File: $finalInstallerPath" -ForegroundColor White
Write-Host "File Size:  $destSizeMb MB" -ForegroundColor White
Write-Host "SHA-256:    $destHash" -ForegroundColor White
Write-Host "Build Time: $elapsedSec seconds" -ForegroundColor White
Write-Host '======================================================================' -ForegroundColor Green
