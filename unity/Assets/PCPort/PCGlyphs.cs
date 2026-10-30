using UnityEngine;
using UnityEngine.SceneManagement;

// PC port: controller button glyphs. PCGlyphs.png (original art, drawn by Tools/make_glyphs.py) holds the images for
// the HUD and the F1 screen. The tips' inline symbols ([AA], [PX]...) are the touch buttons in the menu atlas, so while
// a controller is in use a copy of that atlas with those icons replaced by PCGlyphsSmall.png takes its place.
// In a level each ability icon on the HUD gets its button, and since the touch buttons are hidden on PC, Interact and
// Execution get a prompt of their own when they can be used.
// avp_glyphs: auto (follows the device in use), xbox or playstation (the controller style), keyboard (never glyphs).
public class PCGlyphs : MonoBehaviour
{
	public enum Id
	{
		A, B, X, Y, LB, RB, LT, RT, LS, RS, L3, R3, DUp, DDown, DLeft, DRight, DPad, Menu, View,
		Cross, Circle, Square, Triangle, L1, R1, L2, R2, PSL3, PSR3, Options, Share
	}

	public const string Keyboard = "keyboard";

	public const string Xbox = "xbox";

	public const string PlayStation = "playstation";

	private const int Columns = 8;

	private const int Rows = 4;

	private const int FontTop = 256; // Monkirta_regular_0's place in AVP_Menus_Atlas

	// The font's symbols that are buttons: x, y, width, height in the font's sprite, and the button.
	private static readonly int[,] Symbols =
	{
		{ 69, 232, 24, 23, PCPad.X },    // [AA] [PA]
		{ 94, 232, 24, 23, PCPad.A },    // [AB] [PB]
		{ 119, 232, 24, 23, PCPad.B },   // [PX]
		{ 204, 209, 25, 23, PCPad.B },   // [AX]
		{ 144, 232, 24, 23, PCPad.X },   // [PI]
		{ 230, 209, 25, 23, PCPad.X },   // [AI]
		{ 169, 232, 24, 23, PCPad.Y },   // [PF]
		{ 144, 194, 11, 13, PCPad.Y },   // [AF]
		{ 194, 232, 24, 23, PCPad.DUp }, // [PW]
		{ 179, 208, 24, 23, PCPad.DLeft }, // [AW]
		{ 154, 208, 24, 23, PCPad.DDown }, // [AR]
		{ 219, 233, 18, 23, PCPad.DRight }, // [PC]
		{ 238, 233, 18, 23, PCPad.DLeft }, // [PT]
	};

	private static Texture2D sheet;

	private static Texture2D small;

	private static Texture2D original;

	private static Material atlasMaterial;

	private static Texture2D patched;

	private static string patchedStyle;

	private static string shownStyle;

	private float next;

	private GUIStyle promptStyle;

	// The style of the controller, whichever device is in use now.
	public static string PadStyle
	{
		get
		{
			string g = PCSettings.Get("avp_glyphs", "auto");
			if (g == Xbox || g == PlayStation)
			{
				return g;
			}
			RewiredInputWrapper input = RewiredInputWrapper.Instance;
			string name = (input != null) ? input.GetLastActiveControllerName().ToLowerInvariant() : string.Empty;
			return (name.Contains("playstation") || name.Contains("dualshock") || name.Contains("dualsense") || name.Contains("wireless controller") || name.Contains("ps4") || name.Contains("ps5")) ? PlayStation : Xbox;
		}
	}

	// What to show now: "keyboard" unless a controller is in use.
	public static string Style
	{
		get
		{
			if (PCSettings.Get("avp_glyphs", "auto") == Keyboard)
			{
				return Keyboard;
			}
			RewiredInputWrapper input = RewiredInputWrapper.Instance;
			return (input != null && input.IsUsingJoystick()) ? PadStyle : Keyboard;
		}
	}

	public static bool Active
	{
		get
		{
			return Style != Keyboard;
		}
	}

	public static Id For(int button, string style)
	{
		bool ps = style == PlayStation;
		switch (button)
		{
		case PCPad.A:
			return ps ? Id.Cross : Id.A;
		case PCPad.B:
			return ps ? Id.Circle : Id.B;
		case PCPad.X:
			return ps ? Id.Square : Id.X;
		case PCPad.Y:
			return ps ? Id.Triangle : Id.Y;
		case PCPad.LB:
			return ps ? Id.L1 : Id.LB;
		case PCPad.RB:
			return ps ? Id.R1 : Id.RB;
		case PCPad.LT:
			return ps ? Id.L2 : Id.LT;
		case PCPad.RT:
			return ps ? Id.R2 : Id.RT;
		case PCPad.L3:
			return ps ? Id.PSL3 : Id.L3;
		case PCPad.R3:
			return ps ? Id.PSR3 : Id.R3;
		case PCPad.Start:
			return ps ? Id.Options : Id.Menu;
		case PCPad.Back:
			return ps ? Id.Share : Id.View;
		case PCPad.DUp:
			return Id.DUp;
		case PCPad.DDown:
			return Id.DDown;
		case PCPad.DLeft:
			return Id.DLeft;
		case PCPad.DRight:
			return Id.DRight;
		}
		return Id.DPad;
	}

	public static void Draw(Rect r, Id id)
	{
		if (sheet == null)
		{
			sheet = Resources.Load<Texture2D>("PCGlyphs");
		}
		if (sheet == null)
		{
			return;
		}
		int i = (int)id;
		Rect uv = new Rect((i % Columns) / (float)Columns, (Rows - 1 - i / Columns) / (float)Rows, 1f / Columns, 1f / Rows);
		GUI.DrawTextureWithTexCoords(r, sheet, uv);
	}

	private static Id ForBinding(int binding, string style)
	{
		const int dpad = PCPad.DUp | PCPad.DDown | PCPad.DLeft | PCPad.DRight;
		if ((binding & dpad) != 0 && (binding & (binding - 1)) != 0)
		{
			return Id.DPad;
		}
		return For(binding, style);
	}

	private static bool Shown(GamePlayButton button, out Rect screen, Camera cam)
	{
		screen = new Rect();
		Renderer[] renderers = button.GetComponentsInChildren<Renderer>();
		bool any = false;
		Bounds b = new Bounds();
		foreach (Renderer r in renderers)
		{
			if (!r.enabled)
			{
				continue;
			}
			if (!any)
			{
				b = r.bounds;
				any = true;
			}
			else
			{
				b.Encapsulate(r.bounds);
			}
		}
		if (!any)
		{
			return false;
		}
		Vector3 low = cam.WorldToScreenPoint(b.min);
		Vector3 high = cam.WorldToScreenPoint(b.max);
		screen = Rect.MinMaxRect(low.x, Screen.height - high.y, high.x, Screen.height - low.y);
		return true;
	}

	private void Prompt(float y, float size, Id id, string text)
	{
		if (promptStyle == null)
		{
			promptStyle = new GUIStyle(GUI.skin.label);
			promptStyle.fontStyle = FontStyle.Bold;
			promptStyle.alignment = TextAnchor.MiddleLeft;
			promptStyle.normal.textColor = Color.white;
		}
		promptStyle.fontSize = Mathf.RoundToInt(size * 0.42f);
		float x = Screen.width * 0.5f - size * 2.2f;
		Color old = GUI.color;
		GUI.color = new Color(0f, 0.02f, 0.05f, 0.55f);
		GUI.DrawTexture(new Rect(x - size * 0.2f, y - size * 0.1f, size * 4.8f, size * 1.2f), Texture2D.whiteTexture);
		GUI.color = old;
		Draw(new Rect(x, y, size, size), id);
		GUI.Label(new Rect(x + size * 1.2f, y, size * 3.6f, size), text, promptStyle);
	}

	private void OnGUI()
	{
		if (Event.current.type != EventType.Repaint || !Active || Time.timeScale < 0.01f)
		{
			return;
		}
		GameplayController gc = GameplayController.Instance;
		HUDElements hud = (gc != null && gc.inputController != null) ? gc.inputController.HUD : null;
		if (hud == null || !hud.gameObject.activeInHierarchy || !gc.inputController.HandleUserInput || PCGraphics.WorldCamera() == null)
		{
			return;
		}
		Camera cam = NGUITools.FindCameraForLayer(hud.gameObject.layer);
		if (cam == null || !cam.enabled)
		{
			return;
		}
		string style = Style;
		float size = Screen.height * 0.03f * Mathf.Clamp(PCSettings.GetInt("avp_hud_scale", 100), 50, 150) / 100f;
		foreach (GamePlayButton button in hud.GetComponentsInChildren<GamePlayButton>())
		{
			int binding = PCPad.Binding(button.rewiredAction);
			Rect r;
			if (binding == 0 || button == hud.buttonPause || !Shown(button, out r, cam))
			{
				continue;
			}
			Draw(new Rect(r.xMin - size * 0.3f, r.yMax - size * 0.7f, size, size), ForBinding(binding, style));
		}
		float prompt = Screen.height * 0.055f;
		float y = Screen.height * 0.74f;
		if (hud.buttonExecution != null && hud.buttonExecution.gameObject.activeInHierarchy && hud.buttonExecution.enabled)
		{
			Prompt(y, prompt, For(PCPad.B, style), "Execution");
			y -= prompt * 1.35f;
		}
		if (hud.buttonInterract != null && hud.buttonInterract.gameObject.activeInHierarchy && hud.buttonInterract.enabled)
		{
			Prompt(y, prompt, For(PCPad.X, style), "Interact");
		}
	}

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		atlasMaterial = null;
		shownStyle = null;
	}

	private void Update()
	{
		if (Time.unscaledTime < next)
		{
			return;
		}
		next = Time.unscaledTime + 0.25f;
		string style = Style;
		if (style == shownStyle)
		{
			return;
		}
		shownStyle = style;
		if (atlasMaterial == null)
		{
			foreach (Material m in Resources.FindObjectsOfTypeAll<Material>())
			{
				if (m.name == "AVP_Menus_Atlas")
				{
					atlasMaterial = m;
					break;
				}
			}
		}
		if (atlasMaterial == null)
		{
			return;
		}
		if (original == null)
		{
			original = atlasMaterial.mainTexture as Texture2D;
		}
		if (style == Keyboard)
		{
			atlasMaterial.mainTexture = original;
			return;
		}
		if (patchedStyle != style)
		{
			Patch(style);
		}
		if (patched != null)
		{
			atlasMaterial.mainTexture = patched;
		}
	}

	// The copy of the atlas, with each button symbol redrawn from PCGlyphsSmall.
	private static void Patch(string style)
	{
		if (small == null)
		{
			small = Resources.Load<Texture2D>("PCGlyphsSmall");
		}
		if (small == null || original == null)
		{
			return;
		}
		if (patched == null)
		{
			RenderTexture rt = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
			Graphics.Blit(original, rt);
			RenderTexture old = RenderTexture.active;
			RenderTexture.active = rt;
			patched = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
			patched.ReadPixels(new Rect(0f, 0f, original.width, original.height), 0, 0, false);
			patched.filterMode = original.filterMode;
			patched.wrapMode = original.wrapMode;
			RenderTexture.active = old;
			RenderTexture.ReleaseTemporary(rt);
		}
		for (int s = 0; s < Symbols.GetLength(0); s++)
		{
			int x = Symbols[s, 0];
			int y = Symbols[s, 1];
			int w = Symbols[s, 2];
			int h = Symbols[s, 3];
			int i = (int)For(Symbols[s, 4], style);
			int bottom = original.height - (FontTop + y) - h;
			// Square glyphs stay square inside a symbol that isn't.
			int size = Mathf.Min(w, h);
			int left = (w - size) / 2;
			int low = (h - size) / 2;
			Color[] clear = new Color[w * h];
			patched.SetPixels(x, bottom, w, h, clear);
			for (int py = 0; py < size; py++)
			{
				for (int px = 0; px < size; px++)
				{
					float u = ((i % Columns) + (px + 0.5f) / size) / Columns;
					float v = ((Rows - 1 - i / Columns) + (py + 0.5f) / size) / Rows;
					patched.SetPixel(x + left + px, bottom + low + py, small.GetPixelBilinear(u, v));
				}
			}
		}
		patched.Apply(false);
		patchedStyle = style;
	}
}
