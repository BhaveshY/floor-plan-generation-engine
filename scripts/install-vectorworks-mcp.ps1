param(
    [ValidateSet("Snippet", "ClaudeDesktop", "Cursor", "Custom")]
    [string]$Client = "Snippet",
    [string]$ConfigPath = "",
    [string]$ServerName = "vectorworks-floor-engine",
    [switch]$GlobalTool
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$localDotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
$packageDir = Join-Path $repoRoot "artifacts\packages"
$toolDir = Join-Path $repoRoot ".tools"
$snippetPath = Join-Path $repoRoot "outputs\vectorworks-mcp-client-config.json"

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

function Get-LocalToolCommand {
    $windowsCommand = Join-Path $toolDir "floorplan-vectorworks-mcp.exe"
    if (Test-Path $windowsCommand) {
        return $windowsCommand
    }

    return (Join-Path $toolDir "floorplan-vectorworks-mcp")
}

function Set-JsonProperty {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Value
    )

    if ($Object.PSObject.Properties[$Name]) {
        $Object.PSObject.Properties.Remove($Name)
    }

    $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
}

function Resolve-ClientConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
        return $ConfigPath
    }

    switch ($Client) {
        "ClaudeDesktop" {
            return (Join-Path $env:APPDATA "Claude\claude_desktop_config.json")
        }
        "Cursor" {
            return (Join-Path $env:USERPROFILE ".cursor\mcp.json")
        }
        "Custom" {
            throw "Pass -ConfigPath when -Client Custom is used."
        }
        default {
            return ""
        }
    }
}

function Update-McpConfig {
    param(
        [string]$Path,
        [string]$Name,
        [object]$Server
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    if (Test-Path $Path) {
        $raw = Get-Content -Raw -Path $Path
        if ([string]::IsNullOrWhiteSpace($raw)) {
            $config = [pscustomobject]@{}
        }
        else {
            $config = $raw | ConvertFrom-Json
            if ($config -is [array]) {
                throw "MCP config must be a JSON object: $Path"
            }
        }

        $backup = "$Path.bak-$(Get-Date -Format yyyyMMddHHmmss)"
        Copy-Item -Path $Path -Destination $backup
        Write-Host "Backed up existing config to $backup"
    }
    else {
        $config = [pscustomobject]@{}
    }

    if ($null -eq $config.mcpServers) {
        Set-JsonProperty -Object $config -Name "mcpServers" -Value ([pscustomobject]@{})
    }

    Set-JsonProperty -Object $config.mcpServers -Name $Name -Value $Server
    $config | ConvertTo-Json -Depth 50 | Set-Content -Encoding UTF8 -Path $Path
}

$dotnetPath = Get-Dotnet
$dotnetRoot = Split-Path -Parent $dotnetPath
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $snippetPath) | Out-Null

Write-Host "Packing FloorPlanGeneration.VectorworksMcp..."
& $dotnetPath pack (Join-Path $repoRoot "FloorPlanGeneration.VectorworksMcp") -c Release -o $packageDir
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($GlobalTool) {
    Write-Host "Installing/updating global MCP tool..."
    & $dotnetPath tool update FloorPlanGeneration.VectorworksMcp --global --add-source $packageDir --version 0.1.0
    if ($LASTEXITCODE -ne 0) {
        & $dotnetPath tool install FloorPlanGeneration.VectorworksMcp --global --add-source $packageDir --version 0.1.0
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    $globalCommand = Join-Path $env:USERPROFILE ".dotnet\tools\floorplan-vectorworks-mcp.exe"
    if (Test-Path $globalCommand) {
        $command = $globalCommand
    }
    else {
        $command = "floorplan-vectorworks-mcp"
    }

    $selfTestCommand = $command
}
else {
    New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
    $existingTool = Get-LocalToolCommand
    if (Test-Path $existingTool) {
        Write-Host "Updating local MCP tool in $toolDir..."
        & $dotnetPath tool update FloorPlanGeneration.VectorworksMcp --tool-path $toolDir --add-source $packageDir --version 0.1.0
    }
    else {
        Write-Host "Installing local MCP tool in $toolDir..."
        & $dotnetPath tool install FloorPlanGeneration.VectorworksMcp --tool-path $toolDir --add-source $packageDir --version 0.1.0
    }

    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $command = Get-LocalToolCommand
    $selfTestCommand = $command
}

Write-Host "Running MCP self-test..."
$previousDotnetRoot = $env:DOTNET_ROOT
$env:DOTNET_ROOT = $dotnetRoot
& $selfTestCommand --self-test | Out-Host
$env:DOTNET_ROOT = $previousDotnetRoot
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$serverConfig = [pscustomobject]@{
    command = $command
    args = @("--stdio")
    env = [pscustomobject]@{
        FLOOR_ENGINE_REPO = $repoRoot
        DOTNET_ROOT = $dotnetRoot
    }
}

$servers = [pscustomobject]@{}
Set-JsonProperty -Object $servers -Name $ServerName -Value $serverConfig
$snippet = [pscustomobject]@{
    mcpServers = $servers
}

$snippet | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $snippetPath
Write-Host "Wrote MCP client config snippet to $snippetPath"

if ($Client -ne "Snippet") {
    $targetConfig = Resolve-ClientConfigPath
    Update-McpConfig -Path $targetConfig -Name $ServerName -Server $serverConfig
    Write-Host "Configured $Client MCP server '$ServerName' in $targetConfig"
    Write-Host "Restart $Client so it reloads MCP servers."
}
else {
    Write-Host "To configure a client, merge the snippet above into its MCP config."
}

Write-Host "Done. MCP command: $command --stdio"
