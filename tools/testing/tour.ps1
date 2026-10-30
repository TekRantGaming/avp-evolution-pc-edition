# Visits levels through the game's console (pc_console.txt) and screenshots each one a few seconds into play.
#   tour.ps1 <game folder> <out folder> <level> [<level> ...]
param([string]$dir, [string]$out)
$levels = $args
$c = Join-Path $dir "pc_console.txt"
$o = Join-Path $dir "pc_console_out.txt"
New-Item -ItemType Directory -Force $out | Out-Null
function Cmd($lines, [int]$wait = 2) {
  Set-Content -Path $c -Value $lines -Encoding ascii
  Start-Sleep -Seconds $wait
  if (Test-Path $o) { return (Get-Content $o -Raw) } else { return "" }
}
foreach ($lv in $levels) {
  Cmd @("timescale 1", "level $lv") 3 | Out-Null
  $started = $false
  for ($i = 0; $i -lt 25; $i++) {
    $r = Cmd @("tap") 2
    if ($r -match "started") { $started = $true; break }
  }
  Start-Sleep -Seconds 7
  $r = Cmd @("timescale 0", "scene", "shot $(Join-Path $out "$lv.png")", "visible 6") 3
  Set-Content -Path (Join-Path $out "$lv.txt") -Value $r
  "$lv started=$started"
}
Cmd @("timescale 1") 1 | Out-Null
