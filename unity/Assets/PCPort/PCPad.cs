using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// PC port: Xbox-style controllers are read straight through XInput. The game's Rewired (2016) only knows the pads of
// its day; newer ones (a Razer Wolverine V3, for one) fall to its generic "Standardized Gamepad" profile, which has no
// Windows layout, so the right stick and many buttons did nothing or the wrong thing. RewiredInputWrapper asks here
// first and turns Rewired's joystick maps off while an XInput pad is connected. The bindings are the game's own
// gamepad map; other controllers (a DualShock 4, say) still go through Rewired. Rumble (avp_rumble) goes out here too:
// a small right-side pulse for a strike, a heavier left-side one for taking a hit, both sides in turn for a slice.
public static class PCPad
{
	public const int DUp = 0x1;
	public const int DDown = 0x2;
	public const int DLeft = 0x4;
	public const int DRight = 0x8;
	public const int Start = 0x10;
	public const int Back = 0x20;
	public const int L3 = 0x40;
	public const int R3 = 0x80;
	public const int LB = 0x100;
	public const int RB = 0x200;
	public const int A = 0x1000;
	public const int B = 0x2000;
	public const int X = 0x4000;
	public const int Y = 0x8000;
	public const int LT = 0x10000;
	public const int RT = 0x20000;

	private const int Slots = 4;

	private const int TriggerThreshold = 64;

	[StructLayout(LayoutKind.Sequential)]
	private struct State
	{
		public uint packet;
		public ushort buttons;
		public byte leftTrigger;
		public byte rightTrigger;
		public short lx;
		public short ly;
		public short rx;
		public short ry;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Vibration
	{
		public ushort left;
		public ushort right;
	}

	private struct Pulse
	{
		public float start;
		public float end;
		public float left;
		public float right;
	}

	[DllImport("xinput9_1_0")]
	private static extern int XInputGetState(int user, out State state);

	[DllImport("xinput9_1_0")]
	private static extern int XInputSetState(int user, ref Vibration vibration);

	private static readonly bool[] connected = new bool[Slots];

	private static bool available = true;

	private static int frame = -1;

	private static float nextScan;

	private static int held;

	private static int previous;

	private static Vector2 left;

	private static Vector2 right;

	private static float lastUsed = -1f;

	private static float lastOther = -1f;

	private static readonly List<Pulse> pulses = new List<Pulse>();

	private static float rumbleLeft;

	private static float rumbleRight;

	public static bool Connected { get; private set; }

	// True while the controller is what the player is using: until a key or the mouse is touched after it.
	public static bool InUse
	{
		get
		{
			return Connected && lastUsed >= lastOther;
		}
	}

	public static Vector2 LeftStick
	{
		get
		{
			Poll();
			return left;
		}
	}

	public static Vector2 RightStick
	{
		get
		{
			Poll();
			return right;
		}
	}

	public static int Buttons
	{
		get
		{
			Poll();
			return held;
		}
	}

	public static void NoteKeyboardOrMouse()
	{
		lastOther = Time.unscaledTime;
	}

	// Once a frame. Asking an empty slot is slow, so those are only tried every couple of seconds.
	public static void Poll()
	{
		if (frame == Time.frameCount || !available)
		{
			return;
		}
		frame = Time.frameCount;
		previous = held;
		held = 0;
		left = Vector2.zero;
		right = Vector2.zero;
		bool scan = Time.unscaledTime >= nextScan;
		if (scan)
		{
			nextScan = Time.unscaledTime + 2f;
		}
		bool any = false;
		bool focused = Application.isFocused || PCSettings.GetBool("tune_pad_background", false);
		float deadZone = Mathf.Clamp(PCSettings.GetFloat("avp_stick_deadzone", 0.16f), 0f, 0.9f);
		for (int i = 0; i < Slots; i++)
		{
			if (!connected[i] && !scan)
			{
				continue;
			}
			State s;
			try
			{
				connected[i] = XInputGetState(i, out s) == 0;
			}
			catch (Exception ex)
			{
				if (ex is DllNotFoundException || ex is EntryPointNotFoundException)
				{
					available = false;
					Connected = false;
					Debug.Log("PCPad: XInput is not available: " + ex.Message);
					return;
				}
				throw;
			}
			if (!connected[i])
			{
				continue;
			}
			any = true;
			if (!focused)
			{
				continue;
			}
			held |= s.buttons;
			if (s.leftTrigger >= TriggerThreshold)
			{
				held |= LT;
			}
			if (s.rightTrigger >= TriggerThreshold)
			{
				held |= RT;
			}
			left += Stick(s.lx, s.ly, deadZone);
			right += Stick(s.rx, s.ry, deadZone);
		}
		left = Vector2.ClampMagnitude(left, 1f);
		right = Vector2.ClampMagnitude(right, 1f);
		if (any != Connected)
		{
			Debug.Log("PCPad: XInput controller " + (any ? "connected" : "disconnected"));
		}
		Connected = any;
		if (held != 0 || left != Vector2.zero || right != Vector2.zero)
		{
			lastUsed = Time.unscaledTime;
		}
		UpdateRumble(focused);
	}

	private static void Add(float delay, float seconds, float strongLeft, float strongRight)
	{
		if (!Connected || !InUse || !PCSettings.GetBool("avp_rumble", true))
		{
			return;
		}
		Pulse p = new Pulse();
		p.start = Time.unscaledTime + delay;
		p.end = p.start + seconds;
		p.left = strongLeft;
		p.right = strongRight;
		pulses.Add(p);
	}

	public static void RumbleStrike(bool heavy)
	{
		Add(0f, heavy ? 0.1f : 0.07f, 0f, heavy ? 0.45f : 0.28f);
	}

	public static void RumbleHurt(bool heavy)
	{
		Add(0f, heavy ? 0.3f : 0.18f, heavy ? 1f : 0.7f, 0f);
	}

	public static void RumbleSlice()
	{
		Add(0f, 0.07f, 0.75f, 0.1f);
		Add(0.07f, 0.07f, 0.1f, 0.75f);
		Add(0.14f, 0.11f, 0.8f, 0.8f);
	}

	private static void UpdateRumble(bool focused)
	{
		float now = Time.unscaledTime;
		float l = 0f;
		float r = 0f;
		for (int i = pulses.Count - 1; i >= 0; i--)
		{
			if (now >= pulses[i].end)
			{
				pulses.RemoveAt(i);
			}
			else if (now >= pulses[i].start)
			{
				l = Mathf.Max(l, pulses[i].left);
				r = Mathf.Max(r, pulses[i].right);
			}
		}
		if (!focused || Time.timeScale < 0.01f)
		{
			pulses.Clear();
			l = 0f;
			r = 0f;
		}
		if (l != rumbleLeft || r != rumbleRight)
		{
			SetRumble(l, r);
		}
	}

	private static void SetRumble(float l, float r)
	{
		rumbleLeft = l;
		rumbleRight = r;
		Vibration v = new Vibration();
		v.left = (ushort)(l * 65535f);
		v.right = (ushort)(r * 65535f);
		for (int i = 0; i < Slots; i++)
		{
			if (connected[i])
			{
				XInputSetState(i, ref v);
			}
		}
	}

	// A motor left running would carry on after the game has closed.
	public static void StopRumble()
	{
		pulses.Clear();
		if (available)
		{
			SetRumble(0f, 0f);
		}
	}

	private static Vector2 Stick(short x, short y, float deadZone)
	{
		Vector2 v = new Vector2(x / 32767f, y / 32767f);
		float size = Mathf.Min(v.magnitude, 1f);
		if (size <= deadZone)
		{
			return Vector2.zero;
		}
		return v.normalized * ((size - deadZone) / (1f - deadZone));
	}

	// The buttons for an action: Rewired map 3 (gameplay), 4 (Alien), 5 (Predator) and 1 (menus) of the game.
	public static int Binding(RewiredAction action)
	{
		switch (action)
		{
		case RewiredAction.Select:
			return A;
		case RewiredAction.Back:
			return B;
		case RewiredAction.AttackA:
			return X;
		case RewiredAction.AttackB:
			return A;
		case RewiredAction.RangeShoot:
			return Y;
		case RewiredAction.Execution:
			return B;
		case RewiredAction.SpecialAction:
			return LB;
		case RewiredAction.SpecialAction2:
			return RB;
		case RewiredAction.PauseGameplay:
			return Back | Start;
		case RewiredAction.ShowPath:
			return L3;
		case RewiredAction.AlignCamera:
			return R3;
		case RewiredAction.ToggleWeapon:
			return DLeft | DRight;
		case RewiredAction.RageRecharge:
			return DUp | DDown;
		case RewiredAction.ToggleMeleeWeapon:
			return DUp;
		case RewiredAction.ToggleRangeWeapon:
			return DDown;
		case RewiredAction.Thermal:
			return DLeft;
		case RewiredAction.Cloak:
			return DRight;
		case RewiredAction.FalconnerCall:
		case RewiredAction.ShockwaveStomp:
			return LT;
		}
		return 0;
	}

	public static float Axis(RewiredAction action)
	{
		Poll();
		switch (action)
		{
		case RewiredAction.MoveHorizontal:
			return left.x;
		case RewiredAction.MoveVertical:
			return left.y;
		case RewiredAction.CameraHorizontal:
			return right.x;
		case RewiredAction.CameraVertical:
			return right.y;
		case RewiredAction.NavigateHorizontal:
			return ((held & DRight) != 0) ? 1f : (((held & DLeft) != 0) ? (-1f) : left.x);
		case RewiredAction.NavigateVertical:
			return ((held & DUp) != 0) ? 1f : (((held & DDown) != 0) ? (-1f) : left.y);
		}
		return 0f;
	}

	public static bool Held(RewiredAction action)
	{
		Poll();
		return (held & Binding(action)) != 0;
	}

	public static bool Down(RewiredAction action)
	{
		Poll();
		int mask = Binding(action);
		return (held & mask) != 0 && (previous & mask) == 0;
	}

	public static bool Up(RewiredAction action)
	{
		Poll();
		int mask = Binding(action);
		return (held & mask) == 0 && (previous & mask) != 0;
	}

	public static bool HeldAll(int mask)
	{
		Poll();
		return (held & mask) == mask;
	}
}
