> These are the working notes, written as the port was made. Paths and tool names are from the local project. Some tools they mention, such as `play_fixes.py`, aren't published because they contain text from the game.

# AVP Evolution — Windows port (Unity rebuild)

Source: `AVP Evolution_1.5.1_com.fde.avpevolution.apk`. This is the **OUYA build**: Unity 4.2.1f4, Mono, armeabi-v7a only.
Lepton couldn't be used. It runs only on aarch64 Linux, and its Android image is 64-bit ARM only.

## Layout
| Path | What |
|---|---|
| `Build/Windows64/AVPEvolution.exe` | The game |
| `AVPE_Project/ExportedProject` | Unity project recovered with AssetRipper 2.0.0, then patched for PC. It's a local git repo, and each fix is a commit. |
| `tools/Unity567` | Unity 5.6.7f1 editor, with a Personal licence activated |
| `tools/AssetRipper` | The extractor (headless web UI) |
| `tools/builtin_shaders` | Unity 5.6.7 built-in shader source (MIT), used to restore shaders |

## To build
Run Unity directly.

```
tools\Unity567\Editor\Unity.exe -batchmode -quit -projectPath AVPE_Project\ExportedProject -buildTarget win64 -executeMethod PCPortBuild.BuildWindows64 -pcPortOut Build\Windows64 -logFile build.log
```

A build takes about 5 minutes. In the editor, use **PC Port ▸ Build Windows x64**.

The one-time scene repair steps are already applied and committed. They live in `Assets/Editor`:

| Entry point | What it does | Re-runnable? |
|---|---|---|
| `PCPortUnbatch.RunAll` | Un-batch static meshes | No: it needs the original Unity 4 scene YAML, so it only works on scenes from the first commit |
| `PCPortUnbatch.FixMirroredAll` | Flip mirrored meshes | Guarded by `ProjectSettings/PCPortMirrorFixed.txt` |
| `PCPortLightmaps.RunAll` | Restore lightmaps | Yes |
| `PCPortNavMesh.RunAll` | Rebake NavMeshes | Yes |

## Why levels were missing, and the fixes (2026-10-29)
1. **Static batching.** The Android build static-batched every level. Each renderer pointed at a shared "Combined Mesh" and selected its piece through `m_SubsetIndices`, a Unity 4 field that 5.6 drops. So objects drew the wrong piece in the wrong place.
   - `PCPortUnbatch` rebuilt 14,040 renderers as standalone meshes from the original subsets, with vertices moved back to local space.
2. **Mirrored objects inside-out.** Static batching had baked negative-scale objects with flipped winding. Once moved back to local space, Unity culled their visible faces, so walls, floors and ceilings disappeared.
   - Fixed for 3,846 meshes across 33 levels.
3. **Animation clip names.** AssetRipper renamed clips that shared a name (`Idle` became `Idle_3`), and the legacy `Animation.Play("Idle")` then failed. That broke doors, NPCs, the razor whip and the HUD slide-ins.
   - 135 clips were restored to their original names, checked against the APK with UnityPy.
4. **Baked lighting.** Unity 5 keeps lightmaps, and each renderer's lightmap index and offset, in a LightingDataAsset that the upgraded scenes don't have.
   - `PCLightmapRestore`, added to 32 scenes, reassigns the original lightmaps. They were re-encoded as 2× EXR, because Unity 4 mobile lightmaps were double-LDR. It also restores 19,863 renderer index and offset entries from the original YAML.
   - The lightmap and fog shader variants are forced into the build.
5. **Baked-only lights.** Hundreds of Unity 4 "BakedOnly" lights per level rendered as realtime in Unity 5, on top of the lightmaps.
   - They're now culled at load, as in Unity 4.
6. **NavMesh.** Unity 5.6 can't read Unity 4 NavMesh data, so NPCs couldn't move.
   - Rebaked for all 34 scenes that shipped one.
7. **Raycasts.** `Physics.queriesHitBackfaces = true` at startup restores Unity 4 / PhysX 2 behaviour for the camera's collision Linecasts and other gameplay raycasts.

## Other changes made to the exported project
- **Framework copies removed.** Decompiled Mono.Posix, System.Configuration, System.EnterpriseServices and System.Security would clash with Unity's own copies, so they were removed. So were polyfill folders and all `Properties/AssemblyInfo.cs` files. `P31RestKit` moved into `Assets/Plugins`.
- **Android-only code is a no-op on PC.** That covers `OuyaSDK.OuyaJava.*`, Flurry analytics, `InitAndroidConfigLink`, `nvsyscaps`, `DeviceName`, the Xperia touchpad (`AndroidInput`) and the intro movies (`Handheld`). Each change is marked `// PC port`.
- **Input.** `Assets/Plugins/PCPort/PCInput.cs` wraps every `Input.GetButton*/GetAxis*` call.
  - It turns the XInput d-pad and triggers into the OUYA virtual buttons JB8–JB13.
  - It adds WASD movement, plus mouse and arrow-key camera control. The OUYA axes are joystick-type, so the keys listed on them never worked, and the game's own mouse-camera path is compiled out.
  - The loading screen says "Press A". Click, or press A or Enter.

  | OUYA | Xbox pad | Keyboard / mouse | Gameplay |
  |---|---|---|---|
  | O (JB0) | A | Space (Enter / click in menus) | block / select |
  | U (JB1) | X | Left mouse | attack |
  | Y (JB2) | Y | F | execution |
  | A (JB3) | B | Right mouse | ranged / back |
  | LB / RB | LB / RB | Q / E | special, weapon switch |
  | L3 / R3 | LS / RS click | Tab / V | objective / align camera |
  | D-pad | D-pad | 1–4 | weapon toggles |
  | LT / RT | LT / RT | Left Shift / Left Ctrl | select circle |
  | Menu key | Start | Esc | pause |

  Movement uses WASD or the left stick. The camera uses the mouse, the arrow keys or the right stick.
- **Display.**
  - No TV overscan margin.
  - The game keeps the desktop resolution.
  - It uses D3D11; the project had been locked to D3D9.
  - The resolution dialog is hidden; hold Alt at launch to show it.
- **Shaders.** 51 GLES shaders couldn't be decompiled.
  - 18 Unity built-ins were restored from Unity's official source.
  - 33 game shaders were hand-ported to Cg from the GLSL and surface-shader code embedded in the APK.
- **Script and engine fixes.**
  - `DetonatorSprayHelper` was ported from UnityScript.
  - A decompiler artifact in `Interpolate.cs` was fixed.
  - `LightProbeCompat` handles the light-probe `coefficients` → `bakedProbes` change, and copes with null probes.
  - Unity 4 `Physics.Raycast` argument names, and an unassigned variable in `ByteReader`, were fixed.
  - `IsXperiaPlay` is no longer computed in a static field initializer.
  - Static batching is off in the build. The scenes are already un-batched, and the half-precision UVs made the batcher assert.
- **Debugging aid.** Press **F9** in game to log what's at the centre of the screen to `output_log.txt`: object, shader, lightmap slot and mesh UVs.

## Verified (2026-10-29), by driving the game with simulated input
- **Build:** clean, exit code 0, all 42 scenes. The runtime log shows no exceptions, no missing animations and no NavMesh errors.
- **Predator Mission 4:** the full Marine base renders with baked lighting, including Corridor A's walls, floor and ceiling.
  - WASD movement and mouse attacks work.
  - Doors open (`SlidingDoor`).
  - A Marine NPC is present and attacks.
  - Alien acid effects show.
  - The pause menu works.
- **Alien level:** the full arena and corridors with lighting, legacy animations and the Alien HUD.
- **Thermal vision** (E / RB) renders the heat palette, with NPCs as hot signatures.
- **Pause:** the game pauses and saves when the window regains focus. That's the original mobile behaviour; on PC it pauses when you alt-tab back.

## Not yet verified
- **Gamepad:** no controller was connected during testing. The XInput mapping is untested.
- **Other levels:** only Predator 4 and one Alien level were played. The side missions and the late campaign levels haven't been checked.

## Known limitations
- **This is the OUYA trial build.** Unlocking the full game needed an OUYA Store purchase. That store shut down in 2019, so the game stays in trial mode, and the port doesn't bypass that check.
- The intro and trailer videos are skipped.
- IAP, Flurry, Facebook, Twitter and Google Play services are all inert.
- The ported shaders follow the original math, but a few use approximations: outlines, reflection amount and the depth curve.
- Unity 4 "Auto" lights now also light lightmapped surfaces, a little more than on the original. This is a minor brightness difference.

---

# Google Play v2.1 port (the full game) — `Build/PlayStore/AVPEvolution.exe`

Source: `playstore/com.fde.avpevolution_2.1.apk` plus `playstore/main.808.com.fde.avpevolution.obb`, the user's own Play purchase. It's Unity 5.3.2p2 with Mono, Google Play licensing and billing, and no OUYA code (`IsOuyaBuild = false`), so it's the full game with no unlock step.

The project is `AVPE_Play/ExportedProject`, a local git repo with one commit per fix.

## How it was made
1. **Merge:** the APK and OBB were unpacked into one folder (`playstore/merged`), because AssetRipper only recognises the pair that way.
2. **Export:** AssetRipper exported it in **Hybrid** mode. The obfuscated Rewired input library doesn't recompile as source, so it stays as its original DLL, as does Prime31.
   - Unity 5.6 silently refuses to reference `Aperture.dll`, so the cutscene library uses decompiled source instead (`aperture_to_source.py` re-points 17 assets).
   - AssetRipper exported the plugin DLLs with the Editor platform disabled. They're re-enabled.
3. **Script fixes:** applied by `play_fixes.py`, which is idempotent:
   - Unity 5.6 API changes: social `Authenticate` overloads, and `UnityEngine.AI` for NavMesh types.
   - Decompiler quirks.
   - The Facebook SDK's mis-filed `Editor` folder.
   - The Detonator helper.
   - Android-only code: intro movies, the Xperia touchpad, the OBB downloader, Play Games, Everyplay, push notifications, and the Prime31 billing and Play Games managers.
   - The `IsXperiaPlay` initializer.
   - `Tegra4Device` is true on PC.
4. **Shaders:** all 183 placeholders were replaced.
   - 70 reused from the OUYA port and Unity's built-in source.
   - 7 new ones in `Shader/` (3 Glow are faithful; 4 are approximations: VertexBlend, WetDiffuseReflective, Blend-Max, Multiply-Double).
   - 106 in `Resources/`: NGUI 2.x UI shaders (these caused the black UI boxes), the ShaderWizard blend-mode family, and HeatDistort.
5. **Un-batching:** 17,779 renderers across 33 levels were un-batched, with the mirrored-mesh fix built in.
6. **Lighting and navigation:** clip names didn't need restoring this time. This version ships Unity 5 lighting and NavMesh data, so the OUYA lightmap and NavMesh repairs haven't been applied. Their effect still needs checking in game.

## Later fixes (2026-10-30)
- **Texture formats and lightmaps:** 679 texture metas were set back from Android ETC/ETC2 formats to automatic. The 15 ETC2 lightmaps were decoded to RGBA32 (`decode_etc_textures.py`). Levels were dark and textureless before this.
- **Clip names:** 132 legacy animation clip names were restored, which ends the "Idle not found" errors.
- **Mobile resolution cut:** `ConditionalCompile.SetScreenResolution` halved the resolution on screens over 2000 pixels wide. It now only runs on Android.
- **Touch HUD:** hidden on PC in `JoystickInputHelper.InitializeSettings`, the same way the SHIELD build hides it. Input is Rewired, which has keyboard, mouse and controller maps.
- **Negative-scale objects (`PCPort/PCMirrorFix.cs`):** Unity 5.6 draws un-batched objects with a negative scale wrongly. In the Predator arena the falling water became huge streaked sheets across the screen.
  - At scene load, each such object gets a mesh copy with the signs built in and a positive scale. Children are moved through the same mirror.
  - Skeletons and animated objects are left alone.
  - Found with the console: `hide`, then `scale Falling 2.7 2.7 2.8` made the sheets vanish.
- **Invisible environment (`PCPort/PCRendererFix.cs`):** in most levels Unity 5.6 never drew the un-batched environment renderers, so walls and floors were missing and the camera saw the sky. Alien 4, 6 and 9, Predator 2, 5 and 7 were the worst; even Predator 1 was missing its outer walls.
  - What ruled things out: a copy of the object drew with any shader, and so did a fresh `MeshRenderer`. Mesh, material, lightmap, winding, fog, occlusion and the parent animation were all eliminated with console experiments (`clone`, `rerender`, `lmindex`, `unparent`, `freecam`).
  - The fix: once a level has started (its loading screen gone), each such renderer on an object without scripts is destroyed and re-added the next frame. Only materials, lightmap, shadows, sorting and enabled state are copied; copying the probe, motion or vertex-stream settings brought the problem back.
  - Light probes: their directional terms are scaled down at the same point (`tune_probe_dir`, `tune_probe_scale`).
- **Doubled lightmap offsets (`PCRendererFix.FixLightmapOffsets`):** black surfaces and flat, dark lighting across the game. Static batching bakes each object's lightmap scale and offset into its mesh's `uv2`, and the un-batching kept those UVs, so the offset still on the renderer was applied twice. Every surface read a tiny wrong patch of the atlas, and where that patch was empty it came out solid black (the Predator 1 centre disc, Alien 4's floors).
  - Proof: `Shape021`'s `uv2` is 0.165-0.219 by 0-0.036, inside its renderer's offset rectangle (scale 0.055, offset 0.165, 0).
  - The fix: once a level has started, any lightmapped renderer whose `uv2` already lies inside its rectangle gets the identity offset. A renderer with a real offset, such as Alien 9's `mountain_cave`, keeps it.
  - Exposure and bloom had been tuned against the broken lighting and were retuned afterwards (exposure 0.9, auto-exposure minimum -4, bloom 0.12 at threshold 2.5).
- **Original null references, now guarded:** `RageMeter.SetRageButtonActive` stopped `RageMeter.Start` part way in the Queen mission, and `PlayerModel.ApplyDamage` dropped a hit in SP5 when the melee weapon or attacker was null.
- **Occlusion culling:** off on the world camera. The 5.3 occlusion data hid walls after unbatching.
- **Quality:** the game starts on High once on PC (`PCPORT_HIGH_QUALITY`). The game's own options can still lower it.

## PC settings and graphics (`Assets/PCPort`)
- **`PCSettings`:** reads the launcher's `avp_evolution.toml` beside the exe (or `--avp_settings <file>`).
  - Defaults match the launcher's, including per-preset values.
  - Applies window mode (borderless uses a Win32 popup style), resolution, V-Sync, frame limit, anisotropic filtering, LOD bias, pixel lights, and mouse/stick sensitivity and invert (in `JoystickInputHelper.CameraMovement`).
- **`PCGraphics` and `PCCameraFx`** on the world camera:
  - **FOV:** scales the game's FOV in tangent space, so its own zooms still work.
  - **Draw distance:** the far plane times 1.75 or 3.
  - **Effects:** Unity Post-Processing Stack v1 (MIT) for bloom, AO, FXAA/TAA, ACES and grading; SMAA (Chman, zlib); `PCLightShafts`.
  - **Grading:** per campaign — cold for Alien, warm for Predator — and gentle on purpose. PPv1's contrast and lift crush shadows in this gamma-space game. ACES uses exposure +1.
  - **Shadows:** the game's own shadow light, made soft, at a higher resolution and distance.
- **Upscalers:**
  - **FSR 1:** `PCPort/FSR/Resources/PCFsr.shader` wraps AMD's `ffx_fsr1.h` (EASU, then RCAS). The world camera renders into a low-res target; a camera just above it in depth upscales before the HUD camera draws.
  - **DLSS and XeSS:** through `native/pc_upscalers` (C++ plugin, D3D11). `PCDlss.cs` jitters the projection (Halton), copies depth and motion vectors, and passes the native pointers. Verified on the RTX 4070 Ti: 853x480 to 1280x720.
  - **XeSS on D3D11:** Intel Arc only. On other GPUs it falls back to FSR 1, and so does DLSS without an RTX card.
  - **Runtimes:** `nvngx_dlss.dll` and `libxess_dx11.dll` are copied beside the exe by `PCPortBuild`, and the third-party licences go to `licenses/`.
- **`PCLighting` (Enhanced lighting):** every baked point or spot light gets a realtime twin that lights only the Player and Enemies layers. Characters now pick up torch and fire colour.
- **`PCLauncherBridge`:**
  - When started with `--avp_from_launcher`: the window comes to the front once, and holding Esc (1.5 s) or Back+Start (1 s) quits back to the launcher, with an on-screen bar.
  - `AVP_TEST_BACKGROUND` keeps it from taking focus.
  - The cursor is captured while a level is played with the mouse.
- **`PCWindow`:** finds the game's own window by process. `GetActiveWindow` returns nothing while the launcher is in front, so it can't be used.
- **`PCHud`:** `avp_hud_scale` scales every NGUI `UIAnchor` group. With `avp_hud_edges` false, wider-than-16:9 screens keep the HUD camera to the middle 16:9.
- **`PCKeys`:** adds the keys the Rewired keyboard map lacks: Space (jump/evade), Tab (show the way), V (centre the camera), E (Alien rage recharge), G (falcons/stomp). They are checked in `RewiredInputWrapper.GetButton*`.
- **`PCPad`:** Xbox-style controllers are read straight through XInput. Rewired (2016) doesn't know newer pads: a Razer Wolverine V3 fell to its "Standardized Gamepad" profile, which has no Windows layout, so the right stick and many buttons did nothing. `RewiredInputWrapper` asks `PCPad` first and turns Rewired's joystick maps off while an XInput pad is connected. The bindings copy the game's gamepad map. Other controllers (DualShock 4) still go through Rewired; a pad Rewired doesn't know and that has no XInput mode won't map correctly.
- **Camera input:** the stick's turn speed was divided by the screen size, so 4K turned at a third of the speed; it now uses a fixed 1280x720 reference, times `tune_stick_turn` (2). Rewired's mouse map was off in the prefab and is enabled in `RewiredInputWrapper.Start`. The mouse turns the camera directly in `InputController.HandleInput` (`JoystickInputHelper.MouseLook`, `tune_mouse_look` 0.08 degrees per count), because the game reads input in FixedUpdate, which loses most of the mouse's movement above 50 fps. Show Path moved to Update for the same reason. Rewired's native mouse keeps reading while the window is in the background, so its axes are ignored unless the game has focus.
- **Controller layout:** X attack (hold for heavy), A jump/evade and combo finisher, B execution, Y ranged/lock on, Menu or View pause. Execution and ranged were swapped after tester feedback, so an execution is never one press away from another attack. The guide button no longer pauses, in Rewired's map either. `PCPad.Binding`, the `Symbols` table in `PCGlyphs`, the F1 card and the launcher's Controls page must all agree.
- **Rumble:** `PCPad` sends XInput vibration when `avp_rumble` is on and the controller is in use. `HumanModel.ApplyDamage` gives a small right-side pulse for the player's strikes and a heavy left-side one for hits taken. Cutting kills and the start of an execution give the slice pattern. It stops when paused, unfocused or quitting.
- **Character lights:** a level has 400 to 500 of `PCLighting`'s twins, with a median strength above 2 and ranges of a few metres, so small lamps flared on characters and could pop as the 8 per-pixel slots changed hands. Twins are now `tune_char_lights` 0.25, capped at `tune_char_light_max` 0.8, and lights under `tune_char_light_min_range` 2.5 get none.
- **`PCGlyphs`:** controller glyphs (original art from `Tools/make_glyphs.py`). The tips' `[AA]`-style font symbols are the touch buttons in `AVP_Menus_Atlas`; while a controller is in use a runtime copy of the atlas with those rectangles redrawn takes its place, so tips show button icons inline. HUD ability icons get their button as a badge, and Interact and Execution, whose touch buttons are hidden on PC, get a prompt. The F1 card shows the same images.
- **`PCControls`:** F1 in a level shows a controls card. Its second column is Xbox or PlayStation names (`avp_glyphs`, or detected from the pad). A 7-second hint appears at the first level start.
- **`PCStrings`:** The English touch wording becomes keyboard or controller wording, following whichever was used last. `Localization.Get` and `ActionPushTip.GetReplacedHint` pass through it, so other languages keep their own text. Enter, Space or the pad's A starts a level from its loading screen.
- **Enhanced lighting:** `noforwardadd` was removed from Mobile/Bumped Specular, Mobile/Bumped Diffuse, Mobile/Diffuse and AMG/Bumped Specular RimLight. Without that, the characters ignored every light except one directional.
- **`PCConsole` (debugging):** create `pc_console.txt` beside the exe, then write commands into it. Results go to `pc_console_out.txt`. Commands:
  - inspection: `scene`, `visible`, `ray x y`, `find`, `xform`, `meshinfo`, `mirrored`, `lights`, `component`;
  - changes: `hide`/`show`, `scale`, `tap` (starts a level from "Tap to start"), `shader`, `unique`, `remesh`, `fx <type> on|off`, `set <key> <value>` (also `tune_*` values), `level <scene>`, `timescale`;
  - `shot <file>`, a screenshot without focus.
- **Quick builds:** `-pcPortScenes SplashScreen,MainMenu,PauseMenu,Store,LoadingScene,GamePlayAlien,GamePlayPredator,Predator_L1,Alien_L1` builds in about a minute, against 25 for all 43 scenes.

## Launcher (TRG Launcher 2.0)
- **Code:** `launcher/trg-launcher` (branch `avp-evolution`, local) adds Android APK installs (`InstallSource::kApk`), project settings sections and `local:` art. `launcher/avp_launcher` is this game's project.
- **Payload:** `make_payload.ps1` puts `Build/PlayStore` and the art beside the launcher as `payload/`. Install checks the player's APK package name and OBB, then copies the payload into `games/avp_evolution`.

## Status (2026-10-30)
- **Build:** clean, all 43 scenes.
- **Tour:** all 19 campaign levels, 13 side missions and the bonus level start and render their environments, checked with `tour.ps1` and the console. It covers each level's opening area only, with default settings.
- **Launcher:** installs from the purchased APK and OBB, plays fullscreen at native resolution with the settings applied, and holding Esc returns to the launcher.
- **Controller:** XInput checked with a virtual Xbox 360 pad (ViGEmBus and the `vgamepad` Python package): both sticks, the face buttons, shoulders, LT, D-pad and stick clicks reach the game, and the camera turns.
- **Textures:** `ConditionalCompile.TextureResolutionHigh` was hardcoded false, so the PC build used the 512 character and weapon textures; PC now uses the 1024 set in `Resources/HighResTextures`. `ResourceMaterialLoader` keeps a material's texture if the file isn't found.
- **Upscalers:** DLSS and FSR 1 were checked on the RTX 4070 Ti. XeSS falls back to FSR 1 on non-Intel cards.
- **Still to do:**
  - The white Predator skins glow in brightly lit arenas.
  - DualSense and other non-XInput pads Rewired doesn't know are untested.
  - The payload ships the game's assets; the bring-your-own-APK converter is future work.
