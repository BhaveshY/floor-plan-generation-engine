# Sweeps floorplate dimensions through the same transform the web form applies
# (rect outer ring, core clamped into the plate, entry/core access re-derived
# from the core centre), then asks the engine for variants at each size.
# Reports valid counts and the first blocking diagnostics so dimension-change
# failures can be located precisely.
param(
    [string]$BaseUrl = "http://localhost:5127"
)

$sample = Get-Content "$PSScriptRoot\..\samples\floor-plan-generation\rectangular-core-input.json" -Raw | ConvertFrom-Json

function Probe([double]$w, [double]$d) {
    $input2 = $sample | ConvertTo-Json -Depth 16 | ConvertFrom-Json

    $input2.floorplate.outer.points = @(
        [pscustomobject]@{ x = 0.0; y = 0.0 },
        [pscustomobject]@{ x = $w;  y = 0.0 },
        [pscustomobject]@{ x = $w;  y = $d },
        [pscustomobject]@{ x = 0.0; y = $d }
    )

    # Same core handling as applyCoreFromForm/clampCoreIntoFloorplate: keep the
    # form's 6x6 at (18,8), clamped inside the plate.
    $cw = 6.0; $cd = 6.0
    $cx = [Math]::Min([Math]::Max(18.0, 0.0), [Math]::Max(0.0, $w - $cw))
    $cy = [Math]::Min([Math]::Max(8.0, 0.0), [Math]::Max(0.0, $d - $cd))
    $input2.fixedElements[0].polygon.points = @(
        [pscustomobject]@{ x = $cx;       y = $cy },
        [pscustomobject]@{ x = $cx + $cw; y = $cy },
        [pscustomobject]@{ x = $cx + $cw; y = $cy + $cd },
        [pscustomobject]@{ x = $cx;       y = $cy + $cd }
    )

    # refreshAccessFromCore equivalent.
    $centerX = [Math]::Round($cx + $cw / 2, 3)
    $input2.access.entryPoints = @([pscustomobject]@{ x = $centerX; y = 0.0 })
    $input2.access.verticalCoreAccess = @([pscustomobject]@{ x = $centerX; y = $cy + $cd })

    $body = @{ input = $input2; validateOnly = $false; variants = 4; seed = $input2.project.seed } | ConvertTo-Json -Depth 16
    try {
        $res = Invoke-RestMethod -Uri "$BaseUrl/api/generate" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 30
    } catch {
        return "{0,6} x {1,-5} REQUEST FAILED: {2}" -f $w, $d, $_.Exception.Message
    }

    $diag = @()
    if ($res.output -and $res.output.variants) {
        foreach ($v in $res.output.variants) {
            foreach ($g in @($v.diagnostics)) {
                if ($g -and $g.severity -match 'error|fail') { $diag += $g.code }
            }
        }
    }
    $diag = $diag | Select-Object -Unique | Select-Object -First 3
    "{0,6} x {1,-5} -> {2}/{3} valid {4}" -f $w, $d, $res.validVariantCount, $res.variantCount, ($(if ($diag) { '[' + ($diag -join ', ') + ']' } else { '' }))
}

Write-Output "--- width sweep (depth 22) ---"
foreach ($w in 24, 26, 28, 30, 33, 36.5, 39.5, 42, 45, 50, 55, 60) { Write-Output (Probe $w 22) }
Write-Output "--- depth sweep (width 42) ---"
foreach ($d in 14, 16, 18, 20, 24, 26, 30) { Write-Output (Probe 42 $d) }

Write-Output "--- combo sweep (W x D extremes) ---"
foreach ($pair in @(@(24,14), @(24,30), @(60,14), @(60,30), @(39.5,25.5), @(33,17.5))) { Write-Output (Probe $pair[0] $pair[1]) }

Write-Output "--- core flush against edges (42 x 22) ---"
function ProbeCore([double]$cx, [double]$cy, [double]$cw, [double]$cd) {
    $input2 = $sample | ConvertTo-Json -Depth 16 | ConvertFrom-Json
    $input2.fixedElements[0].polygon.points = @(
        [pscustomobject]@{ x = $cx;       y = $cy },
        [pscustomobject]@{ x = $cx + $cw; y = $cy },
        [pscustomobject]@{ x = $cx + $cw; y = $cy + $cd },
        [pscustomobject]@{ x = $cx;       y = $cy + $cd }
    )
    $centerX = [Math]::Round($cx + $cw / 2, 3)
    $input2.access.entryPoints = @([pscustomobject]@{ x = $centerX; y = 0.0 })
    $input2.access.verticalCoreAccess = @([pscustomobject]@{ x = $centerX; y = $cy + $cd })
    $body = @{ input = $input2; validateOnly = $false; variants = 4; seed = $input2.project.seed } | ConvertTo-Json -Depth 16
    try {
        $res = Invoke-RestMethod -Uri "$BaseUrl/api/generate" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 30
    } catch { return "core($cx,$cy,$cw,$cd) REQUEST FAILED: $($_.Exception.Message)" }
    "core({0},{1},{2}x{3}) -> {4}/{5} valid" -f $cx, $cy, $cw, $cd, $res.validVariantCount, $res.variantCount
}
foreach ($c in @(@(0,8,6,6), @(36,8,6,6), @(18,0,6,6), @(18,16,6,6), @(0,0,6,6), @(18,8,12,6), @(18,8,3,3))) { Write-Output (ProbeCore $c[0] $c[1] $c[2] $c[3]) }

Write-Output "--- rule extremes (42 x 22) ---"
function ProbeRules([double]$corridor, [double]$minUnit, [double]$roomW) {
    $input2 = $sample | ConvertTo-Json -Depth 16 | ConvertFrom-Json
    $input2.rules.minCorridorWidth = $corridor
    $input2.rules.minUnitArea = $minUnit
    $input2.rules.minRoomWidth = $roomW
    $input2.rules.minRoomDepth = $roomW
    $body = @{ input = $input2; validateOnly = $false; variants = 4; seed = $input2.project.seed } | ConvertTo-Json -Depth 16
    try {
        $res = Invoke-RestMethod -Uri "$BaseUrl/api/generate" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 30
    } catch { return "rules(c=$corridor,u=$minUnit,r=$roomW) REQUEST FAILED: $($_.Exception.Message)" }
    "rules(corridor={0}, minUnit={1}, roomW={2}) -> {3}/{4} valid" -f $corridor, $minUnit, $roomW, $res.validVariantCount, $res.variantCount
}
foreach ($r in @(@(1.2,25,2.4), @(3,25,2.4), @(2,15,2.4), @(2,60,2.4), @(2,25,1.5), @(2,25,4), @(3.5,55,3.5))) { Write-Output (ProbeRules $r[0] $r[1] $r[2]) }

Write-Output "--- unit mix extremes (42 x 22) ---"
function ProbeMix([double]$s, [double]$o, [double]$t) {
    $input2 = $sample | ConvertTo-Json -Depth 16 | ConvertFrom-Json
    $input2.program.targetUnitTypes[0].targetRatio = $s
    $input2.program.targetUnitTypes[1].targetRatio = $o
    $input2.program.targetUnitTypes[2].targetRatio = $t
    $body = @{ input = $input2; validateOnly = $false; variants = 4; seed = $input2.project.seed } | ConvertTo-Json -Depth 16
    try {
        $res = Invoke-RestMethod -Uri "$BaseUrl/api/generate" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 30
    } catch { return "mix($s/$o/$t) REQUEST FAILED: $($_.Exception.Message)" }
    "mix(studio={0}, 1br={1}, 2br={2}) -> {3}/{4} valid" -f $s, $o, $t, $res.validVariantCount, $res.variantCount
}
foreach ($m in @(@(1,0,0), @(0,1,0), @(0,0,1), @(0.5,0.5,0), @(0,0,0))) { Write-Output (ProbeMix $m[0] $m[1] $m[2]) }
