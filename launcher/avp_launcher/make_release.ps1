# Makes the Windows release: the launcher, the converter, the port and the upscalers' runtimes, as a zip. It holds no
# game files: the launcher builds the game from the player's own .apk and .obb (see converter/convert.py).
#   usage: powershell -File make_release.ps1 [-Out <folder>]
# Before running it: build the launcher (build.bat) and pack the port (converter/dev/pack_port.py).
param(
  [string]$Out = "$PSScriptRoot\..\..\release"
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\..\.."
$version = (Get-Content "$PSScriptRoot\VERSION" -Raw).Trim()
$stage = Join-Path $Out "AVPEvolutionPC"
$conv = Join-Path $root "converter"
$ext = Join-Path $root "tools\ext"

if (-not (Test-Path "$PSScriptRoot\build\avp_launcher.exe")) { throw "Build the launcher first (build.bat)" }
if (-not (Test-Path "$conv\pack\port\Assets\PCPort")) { throw "Pack the port first (converter/dev/pack_port.py)" }
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force "$stage\converter" | Out-Null

Copy-Item "$PSScriptRoot\build\avp_launcher.exe" $stage
foreach ($f in "convert.py", "apply_patches.py", "patches.json", "shaders.py", "family_shaders.py", "restore_clipnames.py", "decode_etc_textures.py") {
  Copy-Item "$conv\$f" "$stage\converter\$f"
}
robocopy "$conv\pack\port" "$stage\converter\pack\port" /E /NFL /NDL /NJH /NJS /NP | Out-Null

# The runtimes the game build copies beside the exe, and the licences of everything third party.
$runtimes = "dlss\lib\Windows_x86_64\rel\nvngx_dlss.dll", "dlss\LICENSE.txt", "xess\bin\libxess_dx11.dll", "xess\LICENSE.txt",
  "fsr1\license.txt", "ppv1\LICENSE", "smaa\LICENSE.txt"
foreach ($f in $runtimes) {
  $to = Join-Path "$stage\converter\tools\ext" $f
  New-Item -ItemType Directory -Force (Split-Path $to) | Out-Null
  Copy-Item (Join-Path $ext $f) $to
}

# Nothing from the game may be in a release.
$bad = Get-ChildItem -Recurse -File $stage | Where-Object {
  $_.Extension -in ".apk", ".obb", ".assets", ".resS", ".resource", ".unity3d" -or
  ($_.Extension -in ".anim", ".prefab", ".unity" -and $_.FullName -notmatch "\\pack\\port\\Assets\\PostProcessing\\") -or
  $_.Name -in "AVPEvolution.exe", "globalgamemanagers", "grid.png", "hero.png" -or $_.FullName -match "\\(payload|launcher_art|AVPEvolution_Data)\\"
}
if ($bad) { throw "Game files in the release: $($bad.FullName -join ', ')" }

$zip = Join-Path $Out "AVPEvolutionPC-$version-windows-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $stage -DestinationPath $zip
$size = (Get-Item $zip).Length
"release: {0} ({1:N1} MB, {2} files)" -f $zip, ($size / 1MB), (Get-ChildItem -Recurse -File $stage).Count
exit 0
