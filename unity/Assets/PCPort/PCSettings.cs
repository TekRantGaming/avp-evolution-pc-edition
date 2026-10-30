using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

// PC port: the launcher's settings for the game (games/avp_evolution/avp_evolution.toml, the avp_ keys of
// launcher/avp_launcher/src/project.cpp). The launcher writes only the keys the player changed, so every default
// here must match the launcher's. Read once at start; display, frame rate, texture filtering, field of view and draw
// distance are applied here, the image effects by PCGraphics.
public class PCSettings : MonoBehaviour
{
	private static Dictionary<string, string> values;

	private static PCSettings instance;

	// The phone game's gameplay camera field of view; avp_fov widens it by the same ratio, so the game's own zooms
	// (Predator sniping, Rage) still work.
	public const float GameFov = 60f;

	public static string SettingsPath
	{
		get
		{
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 0; i + 1 < args.Length; i++)
			{
				if (args[i] == "--avp_settings")
				{
					return args[i + 1];
				}
			}
			return Path.Combine(Path.GetDirectoryName(Application.dataPath), "avp_evolution.toml");
		}
	}

	public static string Preset
	{
		get
		{
			return Get("avp_preset", "enhanced");
		}
	}

	public static bool FromLauncher
	{
		get
		{
			foreach (string arg in Environment.GetCommandLineArgs())
			{
				if (arg.StartsWith("--avp_from_launcher"))
				{
					return true;
				}
			}
			return false;
		}
	}

	public static float MouseSensitivity
	{
		get
		{
			return GetFloat("avp_mouse_sensitivity", 1f);
		}
	}

	public static float StickSensitivity
	{
		get
		{
			return GetFloat("avp_stick_sensitivity", 1f);
		}
	}

	public static bool InvertY
	{
		get
		{
			return GetBool("avp_invert_y", false);
		}
	}

	public static void Load()
	{
		values = new Dictionary<string, string>();
		string path = SettingsPath;
		try
		{
			if (File.Exists(path))
			{
				foreach (string raw in File.ReadAllLines(path))
				{
					string line = raw.Trim();
					if (line.Length == 0 || line[0] == '#' || line[0] == '[')
					{
						continue;
					}
					int eq = line.IndexOf('=');
					if (eq <= 0)
					{
						continue;
					}
					string key = line.Substring(0, eq).Trim();
					string value = line.Substring(eq + 1).Trim();
					int comment = value.IndexOf(" #", StringComparison.Ordinal);
					if (comment >= 0)
					{
						value = value.Substring(0, comment).Trim();
					}
					if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
					{
						value = value.Substring(1, value.Length - 2);
					}
					values[key] = value;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning("PCSettings: cannot read " + path + ": " + ex.Message);
		}
		Debug.Log("PCSettings: " + values.Count + " settings from " + path + ", preset " + Preset);
	}

	// A preset's value for a graphics key the player has not set themselves (the launcher writes all of them when a
	// preset is picked, so this only matters before the first change).
	private static string PresetDefault(string key)
	{
		string preset = (values != null && values.ContainsKey("avp_preset")) ? values["avp_preset"] : "enhanced";
		bool original = preset == "original";
		bool balanced = preset == "balanced";
		bool ultra = preset == "ultra";
		switch (key)
		{
		case "avp_aa":
			return original ? "off" : (ultra ? "taa" : "smaa");
		case "avp_aniso":
			return original ? "1" : (balanced ? "8" : "16");
		case "avp_bloom":
			return original ? "false" : "true";
		case "avp_ssao":
			return (original || balanced) ? "off" : (ultra ? "high" : "low");
		case "avp_tonemap":
			return original ? "original" : "aces";
		case "avp_grading":
		case "avp_auto_exposure":
			return original ? "false" : "true";
		case "avp_shadows":
			return original ? "off" : (balanced ? "low" : (ultra ? "ultra" : "high"));
		case "avp_light_shafts":
			return (original || balanced) ? "false" : "true";
		case "avp_draw_distance":
			return original ? "original" : (ultra ? "max" : "far");
		case "avp_enhanced_lighting":
			return (original || balanced) ? "false" : "true";
		}
		return null;
	}

	public static string Get(string key, string def)
	{
		if (values == null)
		{
			Load();
		}
		string value;
		if (values.TryGetValue(key, out value) && value.Length > 0)
		{
			return value;
		}
		string preset = PresetDefault(key);
		return preset ?? def;
	}

	// For PCConsole: changes a setting for this run only.
	public static void Set(string key, string value)
	{
		if (values == null)
		{
			Load();
		}
		values[key] = value;
		if (instance != null)
		{
			instance.ApplyQuality();
		}
	}

	public static bool GetBool(string key, bool def)
	{
		string v = Get(key, def ? "true" : "false").ToLowerInvariant();
		if (v == "true" || v == "1" || v == "yes" || v == "on")
		{
			return true;
		}
		if (v == "false" || v == "0" || v == "no" || v == "off")
		{
			return false;
		}
		return def;
	}

	public static int GetInt(string key, int def)
	{
		int n;
		return int.TryParse(Get(key, def.ToString()), out n) ? n : def;
	}

	public static float GetFloat(string key, float def)
	{
		float f;
		return float.TryParse(Get(key, def.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : def;
	}

	// The camera turn the game reads from Rewired, with the player's sensitivity and invert.
	public static Vector2 ScaleLook(Vector2 look, bool mouse)
	{
		look *= mouse ? MouseSensitivity : StickSensitivity;
		if (InvertY)
		{
			look.y = 0f - look.y;
		}
		return look;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		Load();
		GameObject go = new GameObject("PCSettings");
		UnityEngine.Object.DontDestroyOnLoad(go);
		instance = go.AddComponent<PCSettings>();
		go.AddComponent<PCGraphics>();
		go.AddComponent<PCLighting>();
		go.AddComponent<PCMirrorFix>();
		go.AddComponent<PCRendererFix>();
		go.AddComponent<PCLauncherBridge>();
		go.AddComponent<PCHud>();
		go.AddComponent<PCControls>();
		go.AddComponent<PCGlyphs>();
		// The phone game picked Low or Mid for most devices and remembers it; on PC start on High once (the game's own
		// options can still lower it).
		if (EPP.GetInt("PCPORT_HIGH_QUALITY", 0) == 0)
		{
			EPP.SetInt("GRAPHICS_QUALITY", 3);
			EPP.SetInt("GRAPHICS_QUALITY_SET_INITIALLY", 1);
			EPP.SetInt("PCPORT_HIGH_QUALITY", 1);
		}
	}

	private void Start()
	{
		ApplyDisplay();
		ApplyQuality();
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		ApplyQuality();
	}

	private int lastQualityLevel = -1;

	private void Update()
	{
		// The game changes the quality level from its own options (and on level start); put the PC settings back.
		if (QualitySettings.GetQualityLevel() != lastQualityLevel)
		{
			ApplyQuality();
		}
		ApplyFrameRate();
		if (Input.GetKeyDown(KeyCode.Return) && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
		{
			Screen.fullScreen = !Screen.fullScreen;
		}
	}

	// ------------------------------------------------------------------ display

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	private static void ParseResolution(string text, out int width, out int height)
	{
		Resolution native = Screen.currentResolution;
		width = native.width;
		height = native.height;
		int x = text.IndexOf('x');
		int w;
		int h;
		if (x > 0 && int.TryParse(text.Substring(0, x), out w) && int.TryParse(text.Substring(x + 1), out h) && w > 0 && h > 0)
		{
			width = w;
			height = h;
		}
	}

	private void ApplyDisplay()
	{
		if (Application.isEditor)
		{
			return;
		}
		string mode = Get("avp_window_mode", "fullscreen");
		int width;
		int height;
		ParseResolution(Get("avp_resolution", "native"), out width, out height);
		Debug.Log("PCSettings: display " + mode + " " + width + "x" + height);
		if (mode == "windowed")
		{
			Screen.SetResolution(width, height, false);
		}
		else if (mode == "borderless")
		{
			// A window without a frame that covers the main screen; the game draws at the chosen size and Windows
			// scales it when it differs from the screen.
			Screen.SetResolution(width, height, false);
			StartCoroutine(MakeBorderless());
		}
		else
		{
			Screen.SetResolution(width, height, true);
		}
	}

	private System.Collections.IEnumerator MakeBorderless()
	{
		yield return null;
		yield return null;
		IntPtr hwnd = PCWindow.Handle;
		if (hwnd != IntPtr.Zero)
		{
			const int GWL_STYLE = -16;
			const long WS_POPUP = 0x80000000L;
			const long WS_VISIBLE = 0x10000000L;
			SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(WS_POPUP | WS_VISIBLE));
			const uint SWP_FRAMECHANGED = 0x20;
			const uint SWP_SHOWWINDOW = 0x40;
			SetWindowPos(hwnd, IntPtr.Zero, 0, 0, GetSystemMetrics(0), GetSystemMetrics(1), SWP_FRAMECHANGED | SWP_SHOWWINDOW);
		}
	}

	private static void ApplyFrameRate()
	{
		int vsync = GetBool("avp_vsync", true) ? 1 : 0;
		if (QualitySettings.vSyncCount != vsync)
		{
			QualitySettings.vSyncCount = vsync;
		}
		int limit = GetInt("avp_frame_limit", 60);
		int target = (limit <= 0) ? (-1) : limit;
		if (Application.targetFrameRate != target)
		{
			Application.targetFrameRate = target;
		}
	}

	// ------------------------------------------------------------------ quality

	private void ApplyQuality()
	{
		lastQualityLevel = QualitySettings.GetQualityLevel();
		ApplyFrameRate();
		QualitySettings.masterTextureLimit = 0;
		int aniso = Mathf.Clamp(GetInt("avp_aniso", 16), 1, 16);
		if (aniso > 1)
		{
			QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
			Texture.SetGlobalAnisotropicFilteringLimits(aniso, aniso);
		}
		else
		{
			QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
		}
		string draw = Get("avp_draw_distance", "far");
		if (draw != "original")
		{
			// Keep every shader's best path and the most detailed level of detail.
			Shader.globalMaximumLOD = int.MaxValue;
			QualitySettings.lodBias = (draw == "max") ? 4f : 2f;
			QualitySettings.maximumLODLevel = 0;
			QualitySettings.particleRaycastBudget = 4096;
		}
		QualitySettings.pixelLightCount = Mathf.Max(QualitySettings.pixelLightCount, GetBool("avp_enhanced_lighting", true) ? 8 : 2);
		QualitySettings.softVegetation = true;
		QualitySettings.blendWeights = BlendWeights.FourBones;
	}
}
