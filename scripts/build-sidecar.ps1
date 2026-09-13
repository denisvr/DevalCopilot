#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes the .NET host as the single self-contained sidecar executable the Tauri
    shell bundles and launches with --bootstrap-stdin.

.DESCRIPTION
    Explicit, reviewable package inputs only:
      - the DevalCopilot.Api project itself, restored with a locked dependency graph;
      - its production appsettings.json (Logging + AllowedHosts — no secret, no
        connection string, no CORS origin);
      - nothing else. No development appsettings, test credential, Playwright fixture,
        or repository path is referenced by this script or by the production Program.cs
        startup path.

    Output is placed at the exact path and name Tauri's sidecar convention requires for
    the initial win-x64 target: src-tauri/binaries/devalcopilot-api-x86_64-pc-windows-msvc.exe
#>
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$TargetTriple = "x86_64-pc-windows-msvc"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$apiProject = Join-Path $repoRoot "src/backend/DevalCopilot.Api/DevalCopilot.Api.csproj"
$publishDir = Join-Path $repoRoot "src/backend/DevalCopilot.Api/bin/$Configuration/net10.0/$RuntimeIdentifier/publish"
$sidecarDir = Join-Path $repoRoot "src/frontend/DevalCopilot.Frontend/src-tauri/binaries"
$sidecarPath = Join-Path $sidecarDir "devalcopilot-api-$TargetTriple.exe"

# Removed up front, not just overwritten at the end: if any step below fails, the sidecar
# is simply absent and `tauri build`/`tauri dev` fail loudly trying to bundle a missing
# externalBin, rather than a stale binary from a previous successful run being silently
# packaged as if this run had succeeded.
if (Test-Path $sidecarPath) {
    Remove-Item -Path $sidecarPath -Force
}

Write-Host "Restoring DevalCopilot.Api with a locked dependency graph..."
dotnet restore $apiProject --locked-mode
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

Write-Host "Publishing the single self-contained sidecar executable..."
dotnet publish $apiProject `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --no-restore `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true `
    -p:DevalenteGenerateApiClients=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$publishedExe = Join-Path $publishDir "DevalCopilot.Api.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Expected publish output not found at $publishedExe"
}

New-Item -ItemType Directory -Force -Path $sidecarDir | Out-Null
Copy-Item -Path $publishedExe -Destination $sidecarPath -Force

# Fail loudly rather than let a silently-skipped copy go unnoticed.
if (-not (Test-Path $sidecarPath)) {
    throw "Sidecar copy did not produce the expected binary at $sidecarPath"
}

Write-Host "Sidecar published to $sidecarPath"
