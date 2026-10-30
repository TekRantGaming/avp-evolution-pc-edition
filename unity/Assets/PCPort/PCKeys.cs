using UnityEngine;

// PC port: keyboard keys for the actions the game's Rewired keyboard map leaves out (it only covers what the Android
// TV version needed). RewiredInputWrapper's button queries also check these.
//   Space   jump / evade (AttackB, also the right mouse button)
//   Tab     show the path to the objective
//   V       centre the camera behind the player
//   E       rage recharge (Alien) - the Predator's E already toggles ranged weapons
//   G       falconer call / shockwave stomp (Predator)
public static class PCKeys
{
	private static KeyCode[] For(RewiredAction action)
	{
		switch (action)
		{
		case RewiredAction.AttackB:
			return new KeyCode[] { KeyCode.Space };
		case RewiredAction.ShowPath:
			return new KeyCode[] { KeyCode.Tab };
		case RewiredAction.AlignCamera:
			return new KeyCode[] { KeyCode.V };
		case RewiredAction.RageRecharge:
			return IsAlien ? new KeyCode[] { KeyCode.E } : null;
		case RewiredAction.FalconnerCall:
		case RewiredAction.ShockwaveStomp:
			return new KeyCode[] { KeyCode.G };
		}
		return null;
	}

	private static bool IsAlien
	{
		get
		{
			GameplayController gc = GameplayController.Instance;
			return gc != null && gc.WhatToSpawn == PlayerType.Alien;
		}
	}

	public static bool Held(RewiredAction action)
	{
		KeyCode[] keys = For(action);
		if (keys != null)
		{
			foreach (KeyCode k in keys)
			{
				if (Input.GetKey(k))
				{
					return true;
				}
			}
		}
		return false;
	}

	public static bool Down(RewiredAction action)
	{
		KeyCode[] keys = For(action);
		if (keys != null)
		{
			foreach (KeyCode k in keys)
			{
				if (Input.GetKeyDown(k))
				{
					return true;
				}
			}
		}
		return false;
	}

	public static bool Up(RewiredAction action)
	{
		KeyCode[] keys = For(action);
		if (keys != null)
		{
			foreach (KeyCode k in keys)
			{
				if (Input.GetKeyUp(k))
				{
					return true;
				}
			}
		}
		return false;
	}
}
