# Changes to the game's own scripts

The game's scripts are copyrighted, so they aren't in this repository. This list describes every change the port makes to them. Only the port's own added code is shown.

The [converter](../BUILDING.md) makes all of these changes for you: the script changes are in `converter/patches.json`, and the others are stages of `converter/convert.py`. Keep this list up to date when you change a game script, so a reviewer can follow what the patch does.

## Input

### `RewiredInputWrapper.cs`

- Add a `padMapsOff` bool.
- At the end of `Start`, enable the player's mouse map: `player.controllers.maps.SetMapsEnabled(true, Rewired.ControllerType.Mouse, "Gameplay Common");`
- Add an `Update` that calls `PCPad.Poll()`. While `PCPad.Connected`, it turns Rewired's joystick maps off with `SetAllMapsEnabled(false, Rewired.ControllerType.Joystick)`, and rechecks every 120 frames. While the game has focus, a keyboard press, mouse button or mouse movement calls `PCPad.NoteKeyboardOrMouse()`.
- Add an `OnApplicationQuit` that calls `PCPad.StopRumble()`.
- `GetAxis` asks `PCPad.Axis(action)` first and returns any value that isn't zero. It only reads Rewired while `Application.isFocused`, because Rewired's native mouse keeps reading in the background.
- `GetButton`, `GetButtonDown` and `GetButtonUp` also return true for `PCKeys.Held`, `Down` or `Up`, and `PCPad.Held`, `Down` or `Up`.
- While `PCPad.Connected`, `IsUsingJoystick` returns `PCPad.InUse` and `IsUsingMouseKeyboard` returns its opposite.
- `GetLastActiveControllerName` returns `"XInput"` while `PCPad.InUse`.

### `JoystickInputHelper.cs`

- `IsXperiaPlay` becomes a lazily evaluated property. Unity 5 doesn't allow `SystemInfo` calls during construction.
- `CameraMovement` returns zero while `IsUsingMouseKeyboard()`, and passes its result through `PCSettings.ScaleLook` with the launcher's sensitivity and invert setting.
- Add a `MouseLook` property. If the game has focus and the mouse is in use, it returns the camera axes through `PCSettings.ScaleLook`. Otherwise it returns zero.
- `InitAndroidConfigLink` returns at once on anything but Android.
- In `InitializeSettings`, on anything but Android, hide the touch buttons with `ToogleUI(false)` and remove the component, as the SHIELD path does.

### `InputController.cs`

- Move the `ShowPath` check out of `ReadJoystickInput`, which runs in `FixedUpdate` and misses most presses above 50 fps, to the start of `HandleInput`.
- In `ReadJoystickInput`, off Android, use a fixed 1280 x 720 reference instead of the screen size for the camera sweep, and multiply it by `PCSettings.GetFloat("tune_stick_turn", 2f)`. Otherwise the stick turns at a third of the speed at 4K.
- In `HandleInput`, inside the camera and movement check, apply `JoystickInputHelper.MouseLook * PCSettings.GetFloat("tune_mouse_look", 0.08f)` straight to the camera's X and Y, honouring `invertY`. While the mouse has moved in the last 0.25 seconds, set `cameraSwiping` so the camera doesn't align itself.
- `ReadXperiaSecondaryVectors` returns at once off Android.
- At the end of `PerformExecutionOnNearbyEnemies`, inside the check that a sign is enabled, call `PCPad.RumbleSlice()`.

### `HumanModel.cs`

- At the start of `ApplyDamage`: if this model is the `PlayerModel`, is alive and the damage is above zero, call `PCPad.RumbleHurt(attackInfo.damage >= damageHeavyLimit)`. If the attacker is the `PlayerModel` and this model is alive, call `PCPad.RumbleStrike` with the same test.
- Add a private `RumbleSlice(AttackInfo)` that calls `PCPad.RumbleSlice()` if the attacker is the `PlayerModel`, and call it first in the `HorizontalCut`/`Disc` and `VerticalCut`/`Uppercut` cases of `ApplyFatalDamage`.

### `Resources/Rewired Input Manager.prefab`

The gamepad layout, for pads the game's input library reads itself. `PCPad` reads XInput pads directly. In the gameplay map of the gamepad template (`categoryId: 2`):

- Action 8 moves from element 5 to element 8, and action 19 from element 8 to element 5, so execution is on B and ranged on Y.
- The entry for action 12 on element 16 goes, so the guide button no longer pauses.

The converter's `rewired` stage does this.

## Text and tips

### `Localization.cs`

- `Get` passes its result through `PCStrings.Text(key, value)`.

### `ActionPushTip.cs`

- In `ChainedMessage.GetReplacedHint`, off Android, return `PCStrings.Symbols(msg)` at the start.

## Graphics and textures

### `ConditionalCompile.cs`

- `TextureResolutionHigh` returns true off Android, so the 1024 textures are used.
- `Tegra4Device` is true off Android. It reserved the High settings for Tegra 4 tablets.
- The check that halves the resolution on screens over 2000 pixels wide returns at once off Android.

### `ResourceMaterialLoader.cs`

- In `LoadTextureOnMaterial`, if the texture isn't found off Android, log it and return, so the material keeps the texture it has.

### Shaders

- AssetRipper can only export placeholders for the game's compiled shaders. The converter's `shaders.py` and `family_shaders.py` give each one code written for the port, keeping the header from your export, and Unity's own shaders come from its public source.
- `Mobile/Bumped Specular`, `Mobile/Bumped Diffuse`, `Mobile/Diffuse` and `AMG/Bumped Specular RimLight`: no `noforwardadd`, or Enhanced lighting does nothing.

## Crashes in the original code

### `RageMeter.cs`

- The Queen mission's HUD has no collider or controller button here, which stopped `Start` part way. Check `buttonActivateRage`'s collider and `buttonActivateRageController` for null.

### `PlayerModel.cs`

- In `ApplyDamage`, and where it decides whether to animate getting hurt, check `CurrentMeleeWeapon` and `attackInfo.attacker` for null. A hit with no melee weapon or attacker threw an exception.

## Android-only code

- `Consts.cs`: `UseOBBFiles` and `UseGPGS` are true on Android only.
- `MainMenuController.cs`, `SplashScreen.cs`: wrap the fullscreen movie calls in `#if UNITY_ANDROID || UNITY_IOS`.
- `PushNotificationController.cs`: return at once off Android, and when `notificationBridge` is null.
- `Everyplay.cs`: `EveryplayInstance` returns null off Android and iOS.
- `GPGManager.cs`, `GPGMultiplayerManager.cs`, `GPGTurnBasedManager.cs`, `GoogleIABManager.cs`: wrap in `#if UNITY_ANDROID`.

## Unity 5.6 and decompiler fixes

- `AGSSocialLocalUser.cs`, `GameCircleSocial.cs`: add the `Authenticate(Action<bool, string>)` overload from `ILocalUser`, calling the existing one.
- `AlienStrafe`, `AlienWallController`, `BasicAI`, `FollowFixPoint`, `GunnerStaticAttack`, `ObjectiveManager`, `PlasmaGunAttack`, `PulseRifflePanic`, `PulseRifleAttack`, `RangeStrafe`, `SimpleGoTo`, `SniperAttack`, `WallCrawlerAI`: add `using UnityEngine.AI;`.
- `ByteReader.cs`, `ShimLayer.cs`, `ThemeSettings.cs`: give the local the decompiler left unassigned a starting value.
- `Interpolate.cs`: replace the decompiler's mangled `GetEnumerator` calls with a cast to `IEnumerable<float>`.
- `DepthOfFieldScatter.cs`: use `ComputeBufferType.IndirectArguments`.
- `ShaderDatabase.cs`: use `StartCoroutine` in place of `StartCoroutine_Auto`.
- `Detonator Spray Helper`: a UnityScript class whose name C# can't spell. The converter takes the decompiled class from the Decompiled export, renames it `DetonatorSprayHelper`, and keeps the `.meta` GUID so the prefabs still bind. Delete the broken `RotateObject-AnimateAlphaFade.cs`.
- Move the Facebook SDK's mis-filed `Editor` folder so it isn't compiled into the player.

## Project settings

- Target Windows x64, with Direct3D 11 and gamma colour space.
- `Run In Background` on. `PCPortBuild` sets this and copies the runtimes and licences.
