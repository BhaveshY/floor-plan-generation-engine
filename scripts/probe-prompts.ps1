# Sweeps diverse natural-language briefs through /api/prompt/parse and prints
# the sanitized intent each one produced. Server must be running on :5127.
$ErrorActionPreference = "Stop"
$briefs = @(
  "luxury 3 bedroom apartments on an L-shaped tower floor",
  "micro studios for students, 25 x 14 plate",
  "mixed development: 40% studios, 40% one bed, 20% two bed",
  "compact senior housing with wide accessible corridors",
  "Wohnhaus mit grossen familienfreundlichen Wohnungen und hellen Wohnzimmern",
  "2bhkflat 36x20",
  "stepped irregular block with generous family homes",
  "a calm scheme"
)
foreach ($b in $briefs) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  try {
    $body = @{ brief = $b } | ConvertTo-Json
    $r = Invoke-RestMethod "http://localhost:5127/api/prompt/parse" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 140
    $sw.Stop()
    if ($r.ok) {
      $i = $r.intent
      $mix = if ($i.mix) { ($i.mix.PSObject.Properties | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join "," } else { "-" }
      $understood = if ($i.understood) { $i.understood -join " | " } else { "-" }
      "OK  [{0,5:n1}s] `"$b`"" -f $sw.Elapsed.TotalSeconds
      "    dims=$($i.width)x$($i.depth) template=$($i.template) mix=[$mix] corridor=$($i.corridor) minUnit=$($i.minUnit) strict=$($i.strictness)"
      "    understood: $understood"
    } else {
      "ERR [{0,5:n1}s] `"$b`" -> $($r.error)" -f $sw.Elapsed.TotalSeconds
    }
  } catch {
    $sw.Stop()
    "FAIL[{0,5:n1}s] `"$b`" -> $($_.Exception.Message)" -f $sw.Elapsed.TotalSeconds
  }
}
"sweep complete"
