#requires -Version 5.1
<#
.SYNOPSIS
    Regenerates FloorPlanGeneration.Tests/Fixtures/golden-contracts.json from live engine output.

.DESCRIPTION
    Run this whenever an intentional engine change alters generation output (AGENTS.md rule #2:
    "Never hand-edit the numbers"). It runs the CLI over each sample referenced by the existing
    fixture and re-extracts the exact contract fields the golden test pins
    (ContractSchemaTests.GoldenContractFixtures_MatchRepresentativeSeededOutputs).

    The curated `requiredDiagnosticCodes` subset and the `input` filename are carried over from the
    existing fixture; every other value is taken from live output. Variant scores are rounded to
    four decimals, well within the test's 1e-4 tolerance.

    Stop the dev server before running -- it locks the engine DLLs.
#>
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$cli = Join-Path $root "FloorPlanGeneration.Cli\bin\Debug\net8.0\FloorPlanGeneration.Cli.dll"
if (-not (Test-Path $cli)) {
    & $dotnet build (Join-Path $root "FloorPlanGeneration.sln") -c Debug --nologo | Out-Null
}

$fixturePath = Join-Path $root "FloorPlanGeneration.Tests\Fixtures\golden-contracts.json"
$existing = Get-Content $fixturePath -Raw | ConvertFrom-Json

$cases = New-Object System.Collections.ArrayList
foreach ($prev in $existing) {
    $samplePath = Join-Path $root ("samples\floor-plan-generation\" + $prev.input)
    $raw = & $dotnet $cli --input $samplePath | Out-String
    $out = $raw | ConvertFrom-Json

    $variants = New-Object System.Collections.ArrayList
    foreach ($v in $out.variants) {
        [void]$variants.Add([ordered]@{
            id            = $v.variantId
            seed          = $v.seed
            score         = [math]::Round([double]$v.metrics.score, 4)
            units         = @($v.units).Count
            rooms         = @($v.rooms).Count
            corridors     = @($v.corridors).Count
            firstUnit     = if (@($v.units).Count -gt 0) { $v.units[0].id } else { "" }
            topologyNodes = @($v.topology.nodes).Count
            topologyEdges = @($v.topology.edges).Count
        })
    }

    [void]$cases.Add([ordered]@{
        input                   = $prev.input
        projectId               = $out.projectId
        status                  = $out.status
        schemaVersion           = $out.metadata.schemaVersion
        seed                    = $out.metadata.seed
        variantCount            = @($out.variants).Count
        grossArea               = $out.metadata.floorplate.grossArea
        usableArea              = $out.metadata.floorplate.usableArea
        requiredDiagnosticCodes = @($prev.requiredDiagnosticCodes)
        variants                = $variants.ToArray()
    })
}

$json = ConvertTo-Json -InputObject $cases.ToArray() -Depth 8
Set-Content -Path $fixturePath -Value $json -Encoding UTF8
Write-Host "Regenerated $fixturePath ($($cases.Count) cases)."
exit 0
