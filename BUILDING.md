# Building AVP Evolution: PC Edition

> [!NOTE]
> The game's scripts, shaders and assets are copyrighted, so none of them are in this repository. The **converter** rebuilds the game on your PC from your own copy: it exports the Unity project with AssetRipper, adds the port, and builds it with Unity 5.6.7. It runs unattended in 15 to 30 minutes, most of it Unity's build.

## What you need

- **Your own copy of the game:** `com.fde.avpevolution_2.1.apk` and `main.808.com.fde.avpevolution.obb`. See the [README](README.md#which-files-you-need) for how to check them
- **[Unity 5.6.7f1](https://unity.com/releases/editor/archive)** with Windows build support, and a Personal licence. Open Unity and sign in once before you run the converter
- **[AssetRipper 2.0.0](https://github.com/AssetRipper/AssetRipper/releases)**. Other versions may export the game differently, and the converter checks for that
- **Unity's built-in shaders for 5.6.7f1:** `builtin_shaders-5.6.7f1.zip`, from the same [archive](https://unity.com/releases/editor/archive) page as the editor
- **Visual Studio 2022** with the C++ tools, **CMake** and **Ninja**
- **Python 3** with [Pillow](https://python-pillow.org) and [texture2ddecoder](https://github.com/K0lb3/texture2ddecoder): `pip install Pillow texture2ddecoder`
- **About 9 GB** of free space: 7.4 GB of work files and 1.5 GB for the game
- **Git**, and this repository cloned with its submodule:

```bash
git clone --recursive https://github.com/TekRantGaming/avp-evolution-pc-edition.git
```

## What's in this repository

| Folder | What it is |
| --- | --- |
| `converter` | Builds the game from your `.apk` and `.obb`. It holds the port's changes to the game's scripts as patches that carry no game text |
| `unity/Assets/PCPort` | The PC layer: settings, graphics, upscalers, controller, glyphs, lighting and rendering fixes |
| `unity/Assets/Plugins/PCPort` | Code that has to compile before the game's own scripts |
| `unity/Assets/Editor` | Build and un-batching tools |
| `native/pc_upscalers` | The native plugin for DLSS and XeSS |
| `launcher/avp_launcher` | The launcher project |
| `launcher/trg-launcher` | The [TRG Launcher](https://github.com/TekRantGaming/trg-launcher), as a submodule on its `avp-evolution` branch |
| `tools` | Glyph art, older pipeline helpers and test tools |
| `docs` | The notes, and the list of changes to the game's scripts |

## 1. Put the tools in place

The converter looks for its tools in one folder. The commands below use the repository's `tools` folder, which git ignores for these:

| Path | What goes there |
| --- | --- |
| `tools/AssetRipper` | AssetRipper, unzipped, so that `tools/AssetRipper/AssetRipper.GUI.Free.exe` exists |
| `tools/Unity567` | Unity 5.6.7f1, so that `tools/Unity567/Editor/Unity.exe` exists. A copy of an installed editor works |
| `tools/builtin_shaders` | `builtin_shaders-5.6.7f1.zip`, unzipped |
| `tools/ext/dlss` | The [DLSS SDK](https://github.com/NVIDIA/DLSS) |
| `tools/ext/xess` | The [XeSS SDK](https://github.com/intel/xess) |
| `tools/ext/fsr1` | [FidelityFX Super Resolution 1](https://github.com/GPUOpen-Effects/FidelityFX-FSR), for its licence |
| `tools/ext/ppv1` | The [Post-processing Stack v1](https://github.com/Unity-Technologies/PostProcessing) |
| `tools/ext/smaa` | [SMAA for Unity](https://github.com/Chman/SMAA), for its licence |

The game build copies the DLSS and XeSS runtimes and every licence from `tools/ext` beside the game's exe.

## 2. Build the native plugin

```bash
native/pc_upscalers/build.bat
```

Copy `native/pc_upscalers/build/pc_upscalers.dll` to `unity/Assets/Plugins/x86_64`.

## 3. Pack the port

Copy the Post-processing Stack's `PostProcessing` folder (from `tools/ext/ppv1`) to `unity/Assets/PostProcessing`, then gather the port into `converter/pack`:

```bash
python converter/dev/pack_port.py unity/Assets
```

Run this again whenever you change anything in `unity/Assets`.

## 4. Run the converter

```bash
python converter/convert.py --apk <your .apk> --obb <your .obb> --work <work folder> --out <game folder> --tools tools
```

No windows open, and it prints each stage as it goes:

| Stage | What it does |
| --- | --- |
| `merge` | Unpacks the `.apk` and `.obb` into one folder, which is the only way AssetRipper recognises the pair |
| `export` | Exports the Unity project with AssetRipper in Hybrid mode, so the third-party assemblies stay as DLLs |
| `aperture` | Unity 5.6 refuses to reference `Aperture.dll`, so a second, Decompiled export supplies the cutscene library as source and the assets are pointed at it. Detonator's spray helper comes from the same export under a name C# allows |
| `patch` | Applies `patches.json`, the port's changes to the game's scripts. Each file's hash is checked before and after, so a different copy of the game stops here instead of building something broken |
| `rewired` | The gamepad layout for pads the game's input library reads itself: execution on B, ranged on Y, and the guide button no longer pauses |
| `cleanup` | Removes files a single Unity project can't hold |
| `plugins` | Lets the editor load the game's plugin DLLs |
| `shaders` | The Android build only holds compiled shaders, so AssetRipper exports placeholders. `shaders.py` and `family_shaders.py` give each one code written for the port. Unity's own come from its public source |
| `clips` | Puts back animation clip names that AssetRipper numbered, because the game finds clips by name |
| `textures` | Moves textures off Android-only formats and decodes the ETC ones |
| `port` | Copies in the pack from step 3 |
| `unity` | Upgrades the project, un-batches every level and builds the game |

AssetRipper gives files new IDs on every export, so every step finds things by name. To repeat a stage, pass `--stages`, for example `--stages unity`. The `unity` stage only un-batches the first time, because un-batching only works on freshly exported scenes. If a stage stops, its message says where to look: `unity.log` and the AssetRipper logs are in the work folder.

> [!CAUTION]
> The work folder and the game folder hold the game's assets. **Never share them, or attach them to an issue.**

## 5. Build the launcher

```bash
launcher/avp_launcher/build.bat
```

The launcher doesn't run the converter yet: for now, its install copies a built game from `payload` beside it, which `make_payload.ps1` makes from a game folder. The launcher's game art isn't included here: put your own `grid.png`, `hero.png` and `logo.png` in `launcher/payload_extra/launcher_art`, and create that folder if it's missing.

## Working on the port

After a run, `<work folder>/export/ExportedProject` is a complete Unity project. Open it in Unity 5.6.7f1, or build it from the command line:

```bash
Unity.exe -batchmode -quit -projectPath <project> -buildTarget win64 -executeMethod PCPortBuild.BuildWindows64 -pcPortOut <output folder> -pcPortExt <tools/ext> -logFile build.log
```

A full build takes about 25 minutes. For a quick test, add this to build a few scenes in about a minute:

```bash
-pcPortScenes SplashScreen,MainMenu,PauseMenu,Store,LoadingScene,GamePlayAlien,GamePlayPredator,Predator_L1,Alien_L1
```

Unity compiles before it imports new `.cs` files, so after adding one, build twice.

**For a pull request:**

- **The port's own code** (`PCPort`, `Plugins/PCPort`, `Editor/PCPort*`): copy your changed files back into `unity/Assets`.
- **The game's scripts:** never commit them. Describe the change in [docs/GAME_SCRIPT_CHANGES.md](docs/GAME_SCRIPT_CHANGES.md), and regenerate `converter/patches.json` with `converter/dev/make_patches.py`. It diffs against a commit of the plain export, so run `--stages merge,export` first, commit the project, then run the remaining stages.
- **Shaders:** the port's shader code lives in `converter/shaders.py` and `converter/family_shaders.py`. Don't paste in code taken from the game's compiled shaders.

## Testing

Create an empty `pc_console.txt` beside the exe to turn on the debug console. Write commands into that file and read the results from `pc_console_out.txt`. The commands are listed at the top of `unity/Assets/PCPort/PCConsole.cs`:

- `pad` shows what the game sees from a controller
- `audit` lists renderers with no material, shader or texture, bad lightmaps and anything in view that isn't being drawn
- `level <scene>` loads a level, and `tap` starts it
- `rumble strike`, `rumble hurt` and `rumble slice` fire the rumble patterns

`converter/dev/check_build.ps1` starts a build minimised, visits levels and runs the audit in each. `tone_probe.ps1` measures a level with the tone map on and off.

`tools/testing` has a virtual Xbox 360 pad for testing without a controller. It needs [ViGEmBus](https://github.com/nefarius/ViGEmBus) and `pip install vgamepad`. Add `tune_pad_background = true` to `avp_evolution.toml` so the game reads it while unfocused.

`tools/testing/tour.ps1` visits levels through the console and takes a screenshot of each.
