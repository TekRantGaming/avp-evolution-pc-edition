# Development tool: measures a level with the game's tone map on and off, minimised, with the cursor code off.
#   tone_probe.ps1 <game folder> <out folder> <level>
param([string]$dir, [string]$out, [string]$level)
$c = "$dir\pc_console.txt"; $o = "$dir\pc_console_out.txt"; $toml = "$dir\avp_evolution.toml"
New-Item -ItemType Directory -Force $out | Out-Null
Set-Content $toml @("avp_window_mode = windowed", "avp_resolution = 1280x720", "avp_mouse_capture = false", "avp_preset = original", "avp_vsync = false") -Encoding ascii
Set-Content $c "# start" -Encoding ascii; Remove-Item $o -ErrorAction SilentlyContinue
$env:AVP_TEST_BACKGROUND = "1"
$game = Start-Process -FilePath "$dir\AVPEvolution.exe" -WorkingDirectory $dir -WindowStyle Minimized -PassThru
function Cmd($lines, [int]$wait = 2) { Set-Content -Path $c -Value $lines -Encoding ascii; Start-Sleep -Seconds $wait; if (Test-Path $o) { Get-Content $o -Raw } else { "" } }
try {
  Start-Sleep -Seconds 15
  Cmd @("level $level") 4 | Out-Null
  for ($i = 0; $i -lt 25; $i++) { $r = Cmd @("tap", "# $i") 2; if ($r -match "started") { break } }
  Start-Sleep -Seconds 9
  Cmd @("timescale 0", "shot $out\on.png", "# a") 3 | Out-Null
  Cmd @("fx PP_ToneMap off", "# b") 2 | Out-Null
  Cmd @("shot $out\off.png", "# c") 3 | Out-Null
  $r = Cmd @("fx", "visible 10", "# d") 3
  Set-Content "$out\fx.txt" -Value $r
}
finally {
  Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue
  Remove-Item Env:\AVP_TEST_BACKGROUND -ErrorAction SilentlyContinue
}
