# Development tool: starts a build minimised, with the cursor code off and no input sent, and audits a few levels.
#   check_build.ps1 <game folder> <out folder> <level> [<level> ...]
param([string]$dir, [string]$out)
$levels = $args
$c = "$dir\pc_console.txt"; $o = "$dir\pc_console_out.txt"; $toml = "$dir\avp_evolution.toml"
New-Item -ItemType Directory -Force $out | Out-Null
Set-Content $toml @("avp_window_mode = windowed", "avp_resolution = 1280x720", "avp_mouse_capture = false", "avp_preset = ultra", "avp_vsync = false") -Encoding ascii
Set-Content $c "# start" -Encoding ascii; Remove-Item $o -ErrorAction SilentlyContinue
$env:AVP_TEST_BACKGROUND = "1"
$game = Start-Process -FilePath "$dir\AVPEvolution.exe" -WorkingDirectory $dir -WindowStyle Minimized -PassThru
function Cmd($lines, [int]$wait = 2) { Set-Content -Path $c -Value $lines -Encoding ascii; Start-Sleep -Seconds $wait; if (Test-Path $o) { Get-Content $o -Raw } else { "" } }
try {
  Start-Sleep -Seconds 15
  foreach ($lv in $levels) {
    Cmd @("level $lv") 4 | Out-Null
    $started = $false
    for ($i = 0; $i -lt 25; $i++) { $r = Cmd @("tap", "# $i") 2; if ($r -match "started") { $started = $true; break } }
    Start-Sleep -Seconds 9
    $r = Cmd @("audit", "scene", "shot $out\$lv.png") 3
    Set-Content -Path "$out\$lv.txt" -Value $r
    $head = ($r -split "`n" | Where-Object { $_ -match '^[a-z ,]+: \d+|^audit:' }) -join " | "
    "$lv started=$started $head"
  }
}
finally {
  Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
  Remove-Item Env:\AVP_TEST_BACKGROUND -ErrorAction SilentlyContinue
}
Get-Content "$dir\AVPEvolution_Data\output_log.txt" | Select-String -Pattern 'PCRendererFix: \d|ResourceMaterialLoader:|Exception|Missing|missing' | ForEach-Object { $_.Line.Substring(0, [Math]::Min(120, $_.Line.Length)) } | Sort-Object | Get-Unique | Select-Object -First 24
