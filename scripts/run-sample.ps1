param(
    [string]$Sample = "rectangular-core",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$localDotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"

function Get-Dotnet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        return $dotnet.Source
    }

    if (Test-Path $localDotnet) {
        return $localDotnet
    }

    Write-Host "Installing a local .NET 8 SDK for this folder..."
    $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
    & $installScript -Channel 8.0 -InstallDir (Join-Path $repoRoot ".dotnet") -NoPath
    return $localDotnet
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $outputDir = Join-Path $repoRoot "outputs"
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    $Output = Join-Path $outputDir "$Sample-output.json"
}

$dotnetPath = Get-Dotnet
Write-Host "Running sample '$Sample'..."
& $dotnetPath run --project (Join-Path $repoRoot "FloorPlanGeneration.Cli") -- --sample $Sample --output $Output --summary

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Done. Output written to $Output"
