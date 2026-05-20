param(
    [int]$Port = 5127
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$localDotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"

function Test-Dotnet8 {
    param([string]$DotnetPath)

    if ([string]::IsNullOrWhiteSpace($DotnetPath) -or !(Test-Path $DotnetPath)) {
        return $false
    }

    try {
        $sdks = & $DotnetPath --list-sdks 2>$null
        return [bool]($sdks | Where-Object { $_ -match "^8\." } | Select-Object -First 1)
    }
    catch {
        return $false
    }
}

function Get-Dotnet {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet -and (Test-Dotnet8 $dotnet.Source)) {
        return $dotnet.Source
    }

    if (Test-Dotnet8 $localDotnet) {
        return $localDotnet
    }

    Write-Host "Installing a local .NET 8 SDK for this folder..."
    $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript
    & $installScript -Channel 8.0 -InstallDir (Join-Path $repoRoot ".dotnet") -NoPath
    return $localDotnet
}

$dotnetPath = Get-Dotnet
$url = "http://localhost:$Port"
Write-Host "Starting Floor Plan Engine Web at $url"
Start-Process $url
& $dotnetPath run --project (Join-Path $repoRoot "FloorPlanGeneration.Web") --urls $url
