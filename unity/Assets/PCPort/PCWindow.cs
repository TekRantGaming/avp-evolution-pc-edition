using System;
using System.Runtime.InteropServices;
using System.Text;

// PC port: the game's own window, found by process (GetActiveWindow returns nothing while another program, such as
// the launcher, is in front).
public static class PCWindow
{
	private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentProcessId();

	[DllImport("user32.dll")]
	public static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	public static extern bool ShowWindow(IntPtr hWnd, int cmd);

	private static IntPtr found;

	public static IntPtr Handle
	{
		get
		{
			found = IntPtr.Zero;
			uint pid = GetCurrentProcessId();
			EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
			{
				uint owner;
				GetWindowThreadProcessId(hWnd, out owner);
				if (owner != pid || !IsWindowVisible(hWnd))
				{
					return true;
				}
				StringBuilder name = new StringBuilder(64);
				GetClassName(hWnd, name, name.Capacity);
				if (name.ToString() == "UnityWndClass")
				{
					found = hWnd;
					return false;
				}
				return true;
			}, IntPtr.Zero);
			return found;
		}
	}

	// Brings the window to the front (the launcher allows it with AllowSetForegroundWindow).
	public static void ToFront()
	{
		IntPtr hwnd = Handle;
		if (hwnd != IntPtr.Zero)
		{
			ShowWindow(hwnd, 9);
			SetForegroundWindow(hwnd);
		}
	}
}
