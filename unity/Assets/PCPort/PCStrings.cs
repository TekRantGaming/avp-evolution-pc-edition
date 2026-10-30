using System.Collections.Generic;
using UnityEngine;

// PC port: the phone game's touch wording, for keyboard and mouse or a controller (whichever was used last).
// Localization.Get passes every string through Text: an override applies only while the game's text is still the
// English original, so other languages keep their own (touch) wording. Tips' button symbols ([PA], [AB], ...) are
// the controller's glyphs while one is in use (PCGlyphs) and worded for the keyboard otherwise (Symbols, ActionPushTip).
public static class PCStrings
{
	// key -> { hash of the English original, keyboard and mouse, controller }
	private static readonly Dictionary<string, string[]> overrides = new Dictionary<string, string[]>
	{
		{ "TAP_TO_START", new string[] { "2f7a5f93", "Click or press Enter to start", "Press A to start" } },
		{ "TIP_HOW_TO_MOVE", new string[] { "6d615b7e", "Use W A S D to move and the mouse to look around", "Use the left stick to move and the right stick to look around" } },
		{ "TIP_SHOW_OBJECTIVE_ARROW", new string[] { "ac9008b0", "Press Tab to show the arrow pointing to your next objective", "Click the left stick to show the arrow pointing to your next objective" } },
		{ "TIP_EXECUTION_MOVE_ALIEN", new string[] { "64b98a5e", "Press [AX] when the fatality symbol is above an enemy to start an execution move, then quickly push the direction shown to finish it.", "Press [AX] when the fatality symbol is above an enemy to start an execution move, then quickly push the direction shown to finish it." } },
		{ "TIP_EXECUTION_MOVE_PREDATOR", new string[] { "c5667ccb", "Press [PX] when the fatality symbol is above an enemy to start an execution move, then quickly push the direction shown to finish it.", "Press [PX] when the fatality symbol is above an enemy to start an execution move, then quickly push the direction shown to finish it." } },
		{ "TIP_FACEHUGGER_EXECUTION", new string[] { "9250c835", "Press [AX] at the fatality sign to entangle your victim.", "Press [AX] at the fatality sign to entangle your victim." } },
	};

	private static bool Pc
	{
		get
		{
			return Application.platform != RuntimePlatform.Android;
		}
	}

	public static bool UsingController
	{
		get
		{
			return PCGlyphs.Active;
		}
	}

	// FNV-1a: this file doesn't quote the game's text, it only recognises it.
	private static string Hash(string text)
	{
		uint hash = 2166136261u;
		foreach (char c in text)
		{
			hash ^= c;
			hash *= 16777619u;
		}
		return hash.ToString("x8");
	}

	public static string Text(string key, string value)
	{
		string[] o;
		if (!Pc || !overrides.TryGetValue(key, out o) || Hash(value) != o[0])
		{
			return value;
		}
		if (!UsingController)
		{
			return o[1];
		}
		return (PCGlyphs.Style == PCGlyphs.PlayStation) ? o[2].Replace("Press A", "Press Cross") : o[2];
	}

	// The tips' button symbols: A attack, B jump/block, X execution, I interact, F ranged. The tips say "the [AB]
	// button", "the Interact button [AI]" or just "[AA]", so each pattern gets its own wording.
	public static string Symbols(string msg)
	{
		if (!Pc || msg == null || UsingController)
		{
			return msg;
		}
		msg = msg.Replace("the left stick", "W A S D").Replace("Left Stick", "W A S D").Replace("left stick", "W A S D");
		if (msg.IndexOf('[') < 0)
		{
			return msg;
		}
		string[,] names =
		{
			{ "AA", "left mouse button" }, { "PA", "left mouse button" },
			{ "AI", "left mouse button" }, { "PI", "left mouse button" },
			{ "AB", "right mouse button" }, { "PB", "right mouse button" },
			{ "AX", "X key" }, { "PX", "X key" },
			{ "AF", "F key" }, { "PF", "F key" },
		};
		for (int i = 0; i < names.GetLength(0); i++)
		{
			string sym = "[" + names[i, 0] + "]";
			string name = names[i, 1];
			msg = msg.Replace("the " + sym + " button", "the " + name);
			msg = msg.Replace("button " + sym, "button (" + name + ")");
			msg = msg.Replace(sym, "the " + name);
		}
		return msg;
	}
}
