using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// PC port: one-click / batch-mode Windows build.
//   Menu:  PC Port > Build Windows x64
//   CLI:   Unity.exe -batchmode -quit -accept-apiupdate -projectPath <proj> -executeMethod PCPortBuild.BuildWindows64 -logFile build.log
public static class PCPortBuild
{
	private const string ExeName = "AVPEvolution.exe";

	[MenuItem("PC Port/Build Windows x64")]
	public static void BuildWindows64()
	{
		string outDir = GetArg("-pcPortOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Build/PlayStore"));
		Directory.CreateDirectory(outDir);

		ConfigurePlayer();

		string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
		// -pcPortScenes MainMenu,Predator_L1,...: a quick test build with only those scenes.
		string only = GetArg("-pcPortScenes");
		if (!string.IsNullOrEmpty(only))
		{
			string[] names = only.Split(',');
			scenes = scenes.Where(s => names.Contains(Path.GetFileNameWithoutExtension(s))).ToArray();
		}
		if (scenes.Length == 0)
		{
			throw new Exception("PCPortBuild: no enabled scenes in EditorBuildSettings");
		}

		string exePath = Path.Combine(outDir, ExeName);
		string error = BuildPipeline.BuildPlayer(scenes, exePath, BuildTarget.StandaloneWindows64, BuildOptions.None);
		if (!string.IsNullOrEmpty(error))
		{
			// Unity 5.6 counts every logged error (e.g. "Font size and style overrides are only supported
			// for dynamic fonts") as a build error even when the player is written; only fail if it wasn't.
			if (!File.Exists(exePath))
			{
				throw new Exception("PCPortBuild failed: " + error);
			}
			Debug.LogWarning("PCPortBuild: player written, but Unity reported: " + error);
		}
		CopyThirdParty(outDir);
		Debug.Log("PCPortBuild: built " + scenes.Length + " scenes to " + outDir);
	}

	// The upscalers' runtimes go beside the exe, where pc_upscalers.dll finds them (NVIDIA DLSS, Intel XeSS), and the
	// licences of everything third party the port uses into licenses\.
	private static void CopyThirdParty(string outDir)
	{
		string ext = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../tools/ext"));
		string[,] files =
		{
			{ "dlss/lib/Windows_x86_64/rel/nvngx_dlss.dll", "nvngx_dlss.dll" },
			{ "xess/bin/libxess_dx11.dll", "libxess_dx11.dll" },
			{ "dlss/LICENSE.txt", "licenses/NVIDIA DLSS.txt" },
			{ "xess/LICENSE.txt", "licenses/Intel XeSS.txt" },
			{ "fsr1/license.txt", "licenses/AMD FidelityFX Super Resolution 1.txt" },
			{ "ppv1/LICENSE", "licenses/Unity Post Processing Stack.txt" },
			{ "smaa/LICENSE.txt", "licenses/SMAA for Unity.txt" },
		};
		for (int i = 0; i < files.GetLength(0); i++)
		{
			string from = Path.Combine(ext, files[i, 0]);
			string to = Path.Combine(outDir, files[i, 1]);
			if (!File.Exists(from))
			{
				Debug.LogWarning("PCPortBuild: " + from + " not found");
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(to));
			File.Copy(from, to, true);
		}
	}

	private static void ConfigurePlayer()
	{
		PlayerSettings.defaultIsFullScreen = true;
		PlayerSettings.resizableWindow = true;
		PlayerSettings.defaultScreenWidth = 1280;
		PlayerSettings.defaultScreenHeight = 720;
		PlayerSettings.runInBackground = true;
		// Hold Alt while launching to get Unity's resolution/quality dialog.
		PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.HiddenByDefault;
		PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
		// The OUYA project was locked to D3D9; let Unity pick D3D11 on Windows.
		PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, true);
		DisableStaticBatching();
		// The OUYA build relied on the Android menu key; nothing here needs mobile defines.
		PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone, "PC_PORT");
	}

	// The scenes were already static-batched by the original Android build, and the exported meshes
	// keep half-precision UVs that Unity 5.6's batcher asserts on, so don't batch them a second time.
	private static void DisableStaticBatching()
	{
		MethodInfo setBatching = typeof(PlayerSettings).GetMethod("SetBatchingForPlatform", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
		if (setBatching == null)
		{
			Debug.LogWarning("PCPortBuild: PlayerSettings.SetBatchingForPlatform not found; static batching left as is");
			return;
		}
		try
		{
			setBatching.Invoke(null, new object[] { BuildTarget.StandaloneWindows64, 0, 1 });
			setBatching.Invoke(null, new object[] { BuildTarget.StandaloneWindows, 0, 1 });
			Debug.Log("PCPortBuild: static batching disabled for Windows");
		}
		catch (Exception e)
		{
			Debug.LogWarning("PCPortBuild: could not disable static batching: " + e.Message);
		}
	}

	private static string GetArg(string name)
	{
		string[] args = Environment.GetCommandLineArgs();
		int i = Array.IndexOf(args, name);
		return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
	}
}
