# Building AVP Evolution: PC Edition

> [!WARNING]
> This is not a one-click build yet. The game's scripts, shaders and assets are copyrighted, so none of them are in this repository. You recover them from your own copy of the game, then add the port on top. Expect to do some of this by hand, and expect the shader step to take real work.

## What you need

- **Your own copy of the game:** `com.fde.avpevolution_2.1.apk` and `main.808.com.fde.avpevolution.obb`. See the [README](README.md#which-files-you-need) for how to check them
- **[Unity 5.6.7f1](https://unity.com/releases/editor/archive)** with Windows build support and a Personal licence
- **[AssetRipper](https://github.com/AssetRipper/AssetRipper)**
- **Visual Studio 2022** with the C++ tools, **CMake** and **Ninja**
- **Python 3** with [Pillow](https://python-pillow.org), for the tools
- **Git**, and this repository cloned with its submodule:

```bash
git clone --recursive https://github.com/TekRantGaming/avp-evolution-pc-edition.git
```

## What's in this repository

| Folder | What it is |
| --- | --- |
| `unity/Assets/PCPort` | The PC layer: settings, graphics, upscalers, controller, glyphs, lighting and rendering fixes |
| `unity/Assets/Plugins/PCPort` | Code that has to compile before the game's own scripts |
| `unity/Assets/Editor` | Build and un-batching tools |
| `native/pc_upscalers` | The native plugin for DLSS and XeSS |
| `launcher/avp_launcher` | The launcher project |
| `launcher/trg-launcher` | The [TRG Launcher](https://github.com/TekRantGaming/trg-launcher), as a submodule on its `avp-evolution` branch |
| `tools` | Glyph art, pipeline helpers and test tools |
| `docs` | The notes, and the list of changes to the game's scripts |

## 1. Recover the Unity project

1. Unpack the `.apk` and `.obb` into one folder. AssetRipper only recognises the pair that way.
2. Export it with AssetRipper in **Hybrid** mode, so the third-party assemblies stay as DLLs. `tools/pipeline/ar_settings.py` shows the settings used, through AssetRipper's local web API.
3. Open the export once in Unity 5.6.7f1 so it upgrades the project. Unity refuses to reference `Aperture.dll`, so the cutscene library has to come from a Decompiled-mode export: `tools/pipeline/aperture_to_source.py` re-points the assets.

Keep the export in its own git repository, so you can see what each step changes.

## 2. Add the port

1. Copy `unity/Assets` over the project's `Assets` folder.
2. Add the [Post-processing Stack v1](https://github.com/Unity-Technologies/PostProcessing) to `Assets/PostProcessing`.
3. Make the changes in [docs/GAME_SCRIPT_CHANGES.md](docs/GAME_SCRIPT_CHANGES.md). They are small, but the project won't compile without them.

## 3. Restore the shaders

The Android build only holds compiled shaders, so AssetRipper exports placeholders, and those are what you'll see as black or missing surfaces. There are 183 of them. They have to be replaced with source: Unity's [built-in shaders](https://unity.com/releases/editor/archive) for 5.6.7 cover many, and the rest have to be rewritten from what each one does.

The replacements aren't published here because they were rebuilt from the game's own shaders. `tools/pipeline/extract_glsl.py` and `summarize_glsl.py` pull out and summarise the compiled shaders to work from. See step 4 of [the notes](docs/PORTING_NOTES.md#how-it-was-made).

Also take `noforwardadd` out of Mobile/Bumped Specular, Mobile/Bumped Diffuse, Mobile/Diffuse and AMG/Bumped Specular RimLight, or Enhanced lighting does nothing.

## 4. Un-batch the levels

The Android build static-batched every level, in a way Unity 5 can't read. In Unity, run **PC Port > Unbatch Static Meshes (all build scenes)** once, on freshly exported scenes. It splits the meshes apart again. [The notes](docs/PORTING_NOTES.md#why-levels-were-missing-and-the-fixes-2026-10-29) explain why.

## 5. Build the native plugin

Download the [DLSS SDK](https://github.com/NVIDIA/DLSS) into `tools/ext/dlss` and the [XeSS SDK](https://github.com/intel/xess) into `tools/ext/xess`, put Unity at `tools/Unity567`, then run:

```bash
native/pc_upscalers/build.bat
```

Copy `pc_upscalers.dll` to `Assets/Plugins/x86_64` in the project. `nvngx_dlss.dll` and `libxess_dx11.dll` go beside the game's exe. The build script copies them if they're in `tools/ext`.

## 6. Build the game

```bash
Unity.exe -batchmode -quit -projectPath <project> -buildTarget win64 -executeMethod PCPortBuild.BuildWindows64 -pcPortOut <output folder> -logFile build.log
```

A full build takes about 25 minutes. For a quick test, add this to build a few scenes in about a minute:

```bash
-pcPortScenes SplashScreen,MainMenu,PauseMenu,Store,LoadingScene,GamePlayAlien,GamePlayPredator,Predator_L1,Alien_L1
```

Unity compiles before it imports new `.cs` files, so after adding one, build twice.

## 7. Build the launcher

```bash
launcher/avp_launcher/build.bat
```

`make_payload.ps1` puts the built game beside the launcher as `payload`, which the install copies once it has checked your `.apk` and `.obb`. The launcher's game art isn't included here: put your own `grid.png`, `hero.png` and `logo.png` in `launcher/payload_extra/launcher_art`, and create that folder if it's missing.

> [!CAUTION]
> A payload contains the game's assets. **Never share one, or attach one to an issue.**

## Testing

Create an empty `pc_console.txt` beside the exe to turn on the debug console. Write commands into that file and read the results from `pc_console_out.txt`. The commands are listed at the top of `unity/Assets/PCPort/PCConsole.cs`:

- `pad` shows what the game sees from a controller
- `audit` lists renderers with no material, shader or texture, bad lightmaps and anything in view that isn't being drawn
- `level <scene>` loads a level, and `tap` starts it
- `rumble strike`, `rumble hurt` and `rumble slice` fire the rumble patterns

`tools/testing` has a virtual Xbox 360 pad for testing without a controller. It needs [ViGEmBus](https://github.com/nefarius/ViGEmBus) and `pip install vgamepad`. Add `tune_pad_background = true` to `avp_evolution.toml` so the game reads it while unfocused.

`tools/testing/tour.ps1` visits levels through the console and takes a screenshot of each.
