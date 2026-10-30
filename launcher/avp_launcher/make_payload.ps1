# Puts the built game (Build\PlayStore) and the launcher's art beside the launcher as payload\, which the launcher's
# install copies into games\avp_evolution after checking the player's own .apk and .obb.
#   usage: powershell -File make_payload.ps1 [-Out <launcher folder>]
param(
  [string]$Out = "$PSScriptRoot\..\..\AVP_PC_Launcher"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\..\.."
$game = Join-Path $root "Build\PlayStore"
$art = Join-Path $root "launcher\payload_extra"
$payload = Join-Path $Out "payload"
if (-not (Test-Path (Join-Path $game "AVPEvolution.exe"))) { throw "Build the game first: $game" }
if (Test-Path $payload) { Remove-Item -Recurse -Force $payload }
New-Item -ItemType Directory -Force $payload | Out-Null
# Test leftovers stay out: the console file turns on the debug console, a settings file would override the launcher's.
robocopy $game $payload /E /NFL /NDL /NJH /NJS /NP /XF pc_console.txt pc_console_out.txt avp_evolution.toml *.png output_log.txt | Out-Null
robocopy $art $payload /E /NFL /NDL /NJH /NJS /NP | Out-Null
Copy-Item (Join-Path $PSScriptRoot "build\avp_launcher.exe") $Out -Force
$size = (Get-ChildItem -Recurse -File $payload | Measure-Object -Sum Length).Sum
"payload: {0:N0} bytes in {1}" -f $size, $payload
exit 0
