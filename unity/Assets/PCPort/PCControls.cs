using UnityEngine;

// PC port: the controls, shown on F1 in a level (and as a hint for a few seconds the first time a level starts).
// Keys come from the game's Rewired keyboard and mouse maps plus PCKeys; the controller column follows PCPad's bindings
// (Xbox glyphs and names, or PlayStation ones when avp_glyphs says so or a PlayStation pad is in use).
public class PCControls : MonoBehaviour
{
	private bool show;

	private float hintUntil = -1f;

	private bool hinted;

	private GUIStyle title;

	private GUIStyle row;

	private GUIStyle hint;

	private GUIStyle header;

	private static readonly string[] Predator =
	{
		"Move", "W A S D", "LS",
		"Camera", "Mouse", "RS",
		"Attack / use", "Left mouse", "X",
		"Jump / evade", "Right mouse, Space", "A",
		"Ranged / lock on", "F", "Y",
		"Execution", "X", "B",
		"Energy recharger", "R", "LB",
		"Health regenerator", "R", "RB",
		"Melee weapon", "Q", "D-pad up",
		"Ranged weapon", "E", "D-pad down",
		"Thermal vision", "T", "D-pad left",
		"Cloak", "C", "D-pad right",
		"Falcons / stomp", "G", "LT",
		"Show the way", "Tab", "LS click",
		"Centre camera", "V", "RS click",
		"Pause", "Esc", "Menu"
	};

	private static readonly string[] Alien =
	{
		"Move", "W A S D", "LS",
		"Camera", "Mouse", "RS",
		"Attack / use", "Left mouse", "X",
		"Jump / pounce", "Right mouse, Space", "A",
		"Ranged / lock on", "F", "Y",
		"Execution", "X", "B",
		"Special", "R", "LB",
		"Switch attacks", "Q", "D-pad left / right",
		"Rage recharge", "E", "D-pad up / down",
		"Show the way", "Tab", "LS click",
		"Centre camera", "V", "RS click",
		"Pause", "Esc", "Menu"
	};

	private static string PlayStation(string pad)
	{
		switch (pad)
		{
		case "A":
			return "Cross";
		case "B":
			return "Circle";
		case "X":
			return "Square";
		case "Y":
			return "Triangle";
		case "LB":
			return "L1";
		case "RB":
			return "R1";
		case "LT":
			return "L2";
		case "RT":
			return "R2";
		case "LS click":
			return "L3";
		case "RS click":
			return "R3";
		case "Menu":
			return "Options";
		}
		return pad;
	}

	// The glyph for a row of the controller column, or -1 for one with none (the D-pad pairs say so in words).
	private static int Glyph(string pad, string style)
	{
		switch (pad)
		{
		case "A":
			return (int)PCGlyphs.For(PCPad.A, style);
		case "B":
			return (int)PCGlyphs.For(PCPad.B, style);
		case "X":
			return (int)PCGlyphs.For(PCPad.X, style);
		case "Y":
			return (int)PCGlyphs.For(PCPad.Y, style);
		case "LB":
			return (int)PCGlyphs.For(PCPad.LB, style);
		case "RB":
			return (int)PCGlyphs.For(PCPad.RB, style);
		case "LT":
			return (int)PCGlyphs.For(PCPad.LT, style);
		case "LS click":
			return (int)PCGlyphs.For(PCPad.L3, style);
		case "RS click":
			return (int)PCGlyphs.For(PCPad.R3, style);
		case "Menu":
			return (int)PCGlyphs.For(PCPad.Start, style);
		case "LS":
			return (int)PCGlyphs.Id.LS;
		case "RS":
			return (int)PCGlyphs.Id.RS;
		case "D-pad up":
			return (int)PCGlyphs.Id.DUp;
		case "D-pad down":
			return (int)PCGlyphs.Id.DDown;
		case "D-pad left":
			return (int)PCGlyphs.Id.DLeft;
		case "D-pad right":
			return (int)PCGlyphs.Id.DRight;
		case "D-pad left / right":
		case "D-pad up / down":
			return (int)PCGlyphs.Id.DPad;
		}
		return -1;
	}

	public void Toggle()
	{
		show = !show;
	}

	private void Update()
	{
		GameplayController gc = GameplayController.Instance;
		bool inLevel = gc != null && PCGraphics.WorldCamera() != null;
		if (!inLevel)
		{
			show = false;
			return;
		}
		if (!hinted && Time.timeScale > 0.01f)
		{
			hinted = true;
			hintUntil = Time.unscaledTime + 7f;
		}
		if (Input.GetKeyDown(KeyCode.F1))
		{
			show = !show;
		}
	}

	private void Styles()
	{
		if (title != null)
		{
			return;
		}
		int size = Mathf.Max(13, Screen.height / 42);
		title = new GUIStyle(GUI.skin.label);
		title.fontSize = size + 4;
		title.fontStyle = FontStyle.Bold;
		title.normal.textColor = new Color(0.55f, 0.9f, 1f);
		row = new GUIStyle(GUI.skin.label);
		row.fontSize = size;
		row.normal.textColor = Color.white;
		header = new GUIStyle(row);
		header.fontStyle = FontStyle.Bold;
		header.normal.textColor = title.normal.textColor;
		hint = new GUIStyle(row);
		hint.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
	}

	private void OnGUI()
	{
		if (!show && Time.unscaledTime > hintUntil)
		{
			return;
		}
		Styles();
		if (!show)
		{
			string key = PCGlyphs.Active ? "F1 on the keyboard" : "F1";
			GUI.Label(new Rect(Screen.width * 0.02f, Screen.height * 0.93f, Screen.width * 0.6f, row.fontSize * 2f), "Press " + key + " to see the controls", hint);
			return;
		}
		GameplayController gc = GameplayController.Instance;
		string[] rows = (gc != null && gc.WhatToSpawn == PlayerType.Alien) ? Alien : Predator;
		string glyphs = PCGlyphs.PadStyle;
		string padTitle = (glyphs == PCGlyphs.PlayStation) ? "PlayStation" : "Xbox";
		float line = row.fontSize * 1.55f;
		float w = Mathf.Min(Screen.width * 0.9f, row.fontSize * 34f);
		float h = line * (rows.Length / 3 + 3);
		Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
		Color old = GUI.color;
		GUI.color = new Color(0f, 0.02f, 0.05f, 0.82f);
		GUI.DrawTexture(box, Texture2D.whiteTexture);
		GUI.color = old;
		float x = box.x + row.fontSize;
		float y = box.y + row.fontSize * 0.6f;
		GUI.Label(new Rect(x, y, w, line * 1.3f), "Controls", title);
		y += line * 1.4f;
		float c1 = w * 0.38f;
		float c2 = w * 0.33f;
		GUI.Label(new Rect(x + c1, y, c2, line), "Keyboard & mouse", header);
		GUI.Label(new Rect(x + c1 + c2, y, w - c1 - c2, line), padTitle, header);
		y += line * 1.2f;
		for (int i = 0; i + 2 < rows.Length; i += 3)
		{
			GUI.Label(new Rect(x, y, c1, line), rows[i], row);
			GUI.Label(new Rect(x + c1, y, c2, line), rows[i + 1], row);
			int glyph = Glyph(rows[i + 2], glyphs);
			float text = x + c1 + c2;
			if (glyph >= 0)
			{
				PCGlyphs.Draw(new Rect(text, y + line * 0.04f, line * 0.92f, line * 0.92f), (PCGlyphs.Id)glyph);
				text += line * 1.1f;
			}
			GUI.Label(new Rect(text, y, x + w - text, line), (glyphs == PCGlyphs.PlayStation) ? PlayStation(rows[i + 2]) : rows[i + 2], row);
			y += line;
		}
	}
}
