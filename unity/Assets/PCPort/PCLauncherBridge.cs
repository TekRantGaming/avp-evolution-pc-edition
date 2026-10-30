using System;
using System.Runtime.InteropServices;
using UnityEngine;

// PC port: the game's side of TRG Launcher 2.0 (launcher2/game_side in the launcher repo) and the mouse.
//  - Started by the launcher (--avp_from_launcher): the window comes to the front once, and holding Esc for a second
//    and a half, or Back and Start for a second, closes the game so the launcher returns.
//  - AVP_TEST_BACKGROUND set (test runs): nothing takes focus.
//  - The cursor is captured while a level is being played with the mouse, so the mouse turns the camera without
//    stopping at the screen edge; it is free in menus, while paused, and hidden while a controller is used.
public class PCLauncherBridge : MonoBehaviour
{
	private const float EscHold = 1.5f;

	private const float PadHold = 1f;

	private bool fromLauncher;

	private bool background;

	private float held;

	private float holdNeeded = EscHold;

	private int framesToFront = 30;

	private GUIStyle style;

	private void Start()
	{
		fromLauncher = PCSettings.FromLauncher;
		background = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AVP_TEST_BACKGROUND"));
	}

	private void Update()
	{
		if (fromLauncher && !background && framesToFront > 0 && --framesToFront == 0)
		{
			PCWindow.ToFront();
		}
		UpdateReturn();
		UpdateCursor();
		if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0) || PCPad.Down(RewiredAction.Select))
		{
			StartLevel();
		}
	}

	// Dismisses a level's "Tap to start" screen as a click on it would (Enter, Space or the controller's A).
	public static bool StartLevel()
	{
		LevelLoader loader = UnityEngine.Object.FindObjectOfType<LevelLoader>();
		System.Reflection.MethodInfo dismiss = typeof(LevelLoader).GetMethod("backButton_onButtonPressBegin", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		if (loader == null || dismiss == null || loader.backButton == null || !loader.backButton.enabled)
		{
			return false;
		}
		dismiss.Invoke(loader, new object[] { null, EventArgs.Empty });
		return true;
	}

	private void UpdateReturn()
	{
		if (!fromLauncher)
		{
			return;
		}
		bool esc = Input.GetKey(KeyCode.Escape);
		bool pad = PCPad.HeldAll(PCPad.Back | PCPad.Start) || (Input.GetKey(KeyCode.JoystickButton6) && Input.GetKey(KeyCode.JoystickButton7));
		if (esc || pad)
		{
			holdNeeded = pad ? PadHold : EscHold;
			held += Time.unscaledDeltaTime;
			if (held >= holdNeeded)
			{
				Debug.Log("PCLauncherBridge: returning to the launcher");
				Application.Quit();
			}
		}
		else
		{
			held = 0f;
		}
	}

	private void UpdateCursor()
	{
		if (background || !Application.isFocused)
		{
			return;
		}
		bool controller = RewiredInputWrapper.Instance != null && RewiredInputWrapper.Instance.IsUsingJoystick();
		GameplayController gc = GameplayController.Instance;
		bool playing = gc != null && Time.timeScale > 0.01f && PCGraphics.WorldCamera() != null && PCSettings.GetBool("avp_mouse_capture", true);
		CursorLockMode mode = (playing && !controller) ? CursorLockMode.Locked : CursorLockMode.None;
		bool visible = !playing && !controller;
		if (Cursor.lockState != mode)
		{
			Cursor.lockState = mode;
		}
		if (Cursor.visible != visible)
		{
			Cursor.visible = visible;
		}
	}

	private void OnGUI()
	{
		if (held <= 0.15f)
		{
			return;
		}
		if (style == null)
		{
			style = new GUIStyle(GUI.skin.label);
			style.alignment = TextAnchor.MiddleCenter;
			style.fontSize = Mathf.Max(14, Screen.height / 40);
			style.normal.textColor = Color.white;
		}
		float w = Screen.width * 0.3f;
		float h = Screen.height / 90f + 2f;
		float x = (Screen.width - w) * 0.5f;
		float y = Screen.height * 0.88f;
		GUI.Label(new Rect(x, y - style.fontSize * 2f, w, style.fontSize * 1.8f), "Returning to the launcher", style);
		Color old = GUI.color;
		GUI.color = new Color(1f, 1f, 1f, 0.25f);
		GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
		GUI.color = new Color(0.35f, 0.85f, 1f, 0.9f);
		GUI.DrawTexture(new Rect(x, y, w * Mathf.Clamp01(held / holdNeeded), h), Texture2D.whiteTexture);
		GUI.color = old;
	}
}
