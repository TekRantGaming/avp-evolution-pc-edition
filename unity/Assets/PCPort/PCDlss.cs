using System;
using System.Runtime.InteropServices;
using UnityEngine;

// PC port: NVIDIA DLSS and Intel XeSS through the pc_upscalers native plugin (native/pc_upscalers). DLSS needs an
// RTX graphics card and nvngx_dlss.dll beside the game; XeSS on Direct3D 11 needs an Intel Arc graphics card and
// libxess_dx11.dll. Everywhere else PCGraphics falls back to AMD FSR 1. One of them is used per run (Kind).
public static class PCDlss
{
	public const int KindDlss = 1;

	public const int KindXess = 2;

	private const int EventInit = 1;

	private const int EventEvaluate = 2;

	[DllImport("pc_upscalers")]
	private static extern IntPtr PCUp_GetRenderEventFunc();

	[DllImport("pc_upscalers")]
	private static extern int PCUp_Status(int kind);

	[DllImport("pc_upscalers")]
	private static extern int PCUp_LastResult(int kind);

	[DllImport("pc_upscalers")]
	private static extern int PCUp_OptimalSize(int kind, int quality, int outputW, int outputH, out int renderW, out int renderH);

	[DllImport("pc_upscalers")]
	private static extern void PCUp_SetFrame(int kind, IntPtr color, IntPtr depth, IntPtr motion, IntPtr output, float jitterX, float jitterY, float mvScaleX, float mvScaleY, int renderW, int renderH, int outputW, int outputH, int quality, int reset, int depthInverted);

	public static int Kind = KindDlss;

	private static readonly bool[] startedKind = new bool[3];

	private static bool started
	{
		get
		{
			return startedKind[Kind];
		}
		set
		{
			startedKind[Kind] = value;
		}
	}

	public static string Name
	{
		get
		{
			return (Kind == KindXess) ? "Intel XeSS" : "NVIDIA DLSS";
		}
	}

	private static bool missing;

	public static int Status
	{
		get
		{
			if (missing)
			{
				return -100;
			}
			try
			{
				return PCUp_Status(Kind);
			}
			catch (Exception)
			{
				missing = true;
				return -100;
			}
		}
	}

	public static bool Ready
	{
		get
		{
			return started && Status == 1;
		}
	}

	public static bool Failed
	{
		get
		{
			return started && Status < 0;
		}
	}

	public static string StatusText
	{
		get
		{
			switch (Status)
			{
			case -1:
				return "the game is not running on Direct3D 11";
			case -2:
				return "NVIDIA NGX did not start (result " + PCUp_LastResult(Kind).ToString("X8") + ")";
			case -3:
				return "NVIDIA NGX has no capability parameters";
			case -4:
				return (Kind == KindXess) ? "XeSS on Direct3D 11 needs an Intel Arc graphics card" : "this graphics card does not support DLSS";
			case -5:
				return "the graphics driver is too old for DLSS";
			case -6:
				return "DLSS could not be created (result " + PCUp_LastResult(Kind).ToString("X8") + ")";
			case -7:
				return "DLSS failed (result " + PCUp_LastResult(Kind).ToString("X8") + ")";
			case -8:
				return "libxess_dx11.dll is missing";
			case -100:
				return "pc_upscalers.dll is missing";
			}
			return "status " + Status;
		}
	}

	// The launcher's quality names as NVSDK_NGX_PerfQuality_Value (DLSS) or xess_quality_settings_t (XeSS).
	public static int QualityFor(string quality)
	{
		if (Kind == KindXess)
		{
			switch (quality)
			{
			case "ultra":
				return 104;
			case "balanced":
				return 102;
			case "performance":
				return 101;
			case "native":
				return 106;
			default:
				return 103;
			}
		}
		switch (quality)
		{
		case "ultra":
			return 4;
		case "balanced":
			return 1;
		case "performance":
			return 0;
		case "native":
			return 5;
		default:
			return 2;
		}
	}

	public static void Init()
	{
		if (started)
		{
			return;
		}
		started = true;
		if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11)
		{
			missing = true;
			Debug.Log("PCDlss: " + Name + " needs Direct3D 11, the game runs on " + SystemInfo.graphicsDeviceType);
			return;
		}
		try
		{
			GL.IssuePluginEvent(PCUp_GetRenderEventFunc(), (Kind << 8) | EventInit);
		}
		catch (Exception ex)
		{
			missing = true;
			Debug.Log("PCDlss: " + ex.Message);
		}
	}

	public static bool OptimalSize(int quality, int outputW, int outputH, out int renderW, out int renderH)
	{
		renderW = renderH = 0;
		if (!Ready)
		{
			return false;
		}
		return PCUp_OptimalSize(Kind, quality, outputW, outputH, out renderW, out renderH) != 0 && renderW > 0 && renderH > 0;
	}

	public static void Evaluate(IntPtr color, IntPtr depth, IntPtr motion, IntPtr output, Vector2 jitter, int renderW, int renderH, int outputW, int outputH, int quality, bool reset)
	{
		// Jitter: how far the projection moved the image, in the textures' own pixels (Unity keeps render textures
		// bottom row first on Direct3D, so up in the image is down the texture's rows, which DLSS calls +y).
		// Motion vectors: Unity's are (now - before) in UV units; DLSS wants pixels from now back to before.
		float jx = PCSettings.GetFloat("tune_dlss_jx", 1f) * jitter.x;
		float jy = PCSettings.GetFloat("tune_dlss_jy", 1f) * jitter.y;
		float mx = PCSettings.GetFloat("tune_dlss_mx", -1f) * renderW;
		float my = PCSettings.GetFloat("tune_dlss_my", -1f) * renderH;
		bool inverted = SystemInfo.usesReversedZBuffer;
		PCUp_SetFrame(Kind, color, depth, motion, output, jx, jy, mx, my, renderW, renderH, outputW, outputH, quality, reset ? 1 : 0, inverted ? 1 : 0);
		GL.IssuePluginEvent(PCUp_GetRenderEventFunc(), (Kind << 8) | EventEvaluate);
	}
}

// On the world camera, last of its image effects: copies the frame's depth and motion vectors into textures of its
// own for DLSS, and owns the full resolution texture DLSS writes.
public class PCDlssCapture : MonoBehaviour
{
	public bool reset = true;

	// This frame's projection jitter in pixels, set by PCCameraFx just before the camera draws.
	public Vector2 jitter;

	private Camera cam;

	private Material material;

	private RenderTexture color;

	private RenderTexture depth;

	private RenderTexture motion;

	private RenderTexture output;

	private IntPtr colorPtr;

	private IntPtr depthPtr;

	private IntPtr motionPtr;

	private IntPtr outputPtr;

	private int quality;

	private void OnEnable()
	{
		cam = GetComponent<Camera>();
		cam.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
		reset = true;
	}

	// Called by PCCameraFx every frame before the camera draws: the low resolution target and the DLSS quality.
	public void Prepare(RenderTexture target, int dlssQuality)
	{
		quality = dlssQuality;
		if (color != target)
		{
			color = target;
			colorPtr = target.GetNativeTexturePtr();
			Release(ref depth);
			Release(ref motion);
			reset = true;
		}
		if (depth == null)
		{
			depth = new RenderTexture(target.width, target.height, 0, RenderTextureFormat.RFloat);
			depth.name = "PCPort DLSS depth";
			depth.Create();
			depthPtr = depth.GetNativeTexturePtr();
			motion = new RenderTexture(target.width, target.height, 0, RenderTextureFormat.RGHalf);
			motion.name = "PCPort DLSS motion";
			motion.Create();
			motionPtr = motion.GetNativeTexturePtr();
		}
	}

	private void OnRenderImage(RenderTexture src, RenderTexture dst)
	{
		Graphics.Blit(src, dst);
		if (depth == null)
		{
			return;
		}
		if (material == null)
		{
			Shader shader = Shader.Find("Hidden/PCPort/DlssInputs");
			if (shader == null)
			{
				return;
			}
			material = new Material(shader);
			material.hideFlags = HideFlags.HideAndDontSave;
		}
		Graphics.Blit(src, depth, material, 0);
		Graphics.Blit(src, motion, material, 1);
	}

	// Runs DLSS from the low resolution frame into a texture of the screen's size; null when it cannot.
	public RenderTexture Evaluate(int w, int h)
	{
		if (color == null || depth == null || !PCDlss.Ready)
		{
			return null;
		}
		if (output == null || output.width != w || output.height != h)
		{
			Release(ref output);
			output = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
			output.name = "PCPort DLSS output";
			output.enableRandomWrite = true;
			output.Create();
			outputPtr = output.GetNativeTexturePtr();
			reset = true;
		}
		PCDlss.Evaluate(colorPtr, depthPtr, motionPtr, outputPtr, jitter, color.width, color.height, w, h, quality, reset);
		reset = false;
		return output;
	}

	private static void Release(ref RenderTexture rt)
	{
		if (rt != null)
		{
			rt.Release();
			UnityEngine.Object.Destroy(rt);
			rt = null;
		}
	}

	private void OnDestroy()
	{
		Release(ref depth);
		Release(ref motion);
		Release(ref output);
		if (material != null)
		{
			UnityEngine.Object.Destroy(material);
		}
	}
}
