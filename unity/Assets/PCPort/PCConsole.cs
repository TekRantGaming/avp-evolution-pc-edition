using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// PC port: a debugging console driven by a file, so the port can be inspected and tuned without rebuilding or
// sending input to the window. Write commands (one per line) to pc_console.txt beside the exe; each time the file
// changes they run and the results go to pc_console_out.txt (and output_log.txt). Off unless that file exists.
//   transparent [n]          the largest visible transparent renderers
//   find <text>              renderers whose object path contains text
//   hide <text> / show <text>  turn those renderers off or on
//   set <key> <value>        change a launcher setting (or a tune_ value) for this run, then rebuild the effects
//   lights                   the scene's lights
//   shot <file.png>          screenshot (a path, or a name in the exe's folder)
//   component <text>         the components on objects whose path contains text
//   ray <x> <y>              what the view ray through that point of the screen (0..1) passes
//   visible [n]              the visible renderers covering the most of the screen
//   fx [type on|off]         the world camera's components, or turn one on or off
//   timescale <x>            Time.timeScale
//   scene                    the loaded scenes and the camera
//   pad                      what Rewired sees: joysticks, their axes and buttons, the player's maps and actions
//   controls                 show or hide the F1 controls screen
//   rumble strike|hurt|slice fire a rumble pattern
//   tip <text>               show a tip the way the game does (symbols like [AB] work)
//   audit                    what is wrong with the renderers in use: no material, shader or texture, bad lightmap,
//                            stale mesh bounds, in view but not drawn
public class PCConsole : MonoBehaviour
{
	private string path;

	private DateTime stamp;

	private float nextCheck;

	private StringBuilder output = new StringBuilder();

	private static readonly Dictionary<string, List<Renderer>> hidden = new Dictionary<string, List<Renderer>>();

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Install()
	{
		string p = Path.Combine(Path.GetDirectoryName(Application.dataPath), "pc_console.txt");
		if (!File.Exists(p))
		{
			return;
		}
		GameObject go = new GameObject("PCConsole");
		go.hideFlags = HideFlags.HideAndDontSave;
		UnityEngine.Object.DontDestroyOnLoad(go);
		PCConsole c = go.AddComponent<PCConsole>();
		c.path = p;
		c.stamp = File.GetLastWriteTimeUtc(p);
		Debug.Log("PCConsole: watching " + p);
	}

	private void Update()
	{
		if (Time.unscaledTime < nextCheck)
		{
			return;
		}
		nextCheck = Time.unscaledTime + 0.5f;
		DateTime t;
		try
		{
			t = File.GetLastWriteTimeUtc(path);
		}
		catch (Exception)
		{
			return;
		}
		if (t == stamp)
		{
			return;
		}
		stamp = t;
		output.Length = 0;
		string[] lines;
		try
		{
			lines = File.ReadAllLines(path);
		}
		catch (Exception ex)
		{
			Debug.Log("PCConsole: " + ex.Message);
			return;
		}
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == '#')
			{
				continue;
			}
			Out("> " + line);
			try
			{
				Run(line);
			}
			catch (Exception ex)
			{
				Out("error: " + ex);
			}
		}
		string text = output.ToString();
		Debug.Log("PCConsole:\n" + text);
		try
		{
			File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), "pc_console_out.txt"), text);
		}
		catch (Exception)
		{
		}
	}

	private void Out(string s)
	{
		output.AppendLine(s);
	}

	private void Note(Dictionary<string, List<string>> found, string kind, string what)
	{
		List<string> list;
		if (!found.TryGetValue(kind, out list))
		{
			list = new List<string>();
			found[kind] = list;
		}
		list.Add(what);
	}

	private static bool Stale(Mesh mesh)
	{
		Vector3[] v = mesh.vertices;
		if (v.Length == 0)
		{
			return false;
		}
		Bounds b = new Bounds(v[0], Vector3.zero);
		foreach (Vector3 p in v)
		{
			b.Encapsulate(p);
		}
		float eps = 0.01f + b.size.magnitude * 0.01f;
		return (b.min - mesh.bounds.min).magnitude > eps || (b.max - mesh.bounds.max).magnitude > eps;
	}

	private void Audit(Camera cam)
	{
		Dictionary<string, List<string>> found = new Dictionary<string, List<string>>();
		Dictionary<Mesh, bool> stale = new Dictionary<Mesh, bool>();
		Plane[] planes = (cam != null) ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
		int total = 0;
		foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
		{
			if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
			{
				continue;
			}
			total++;
			string name = PathOf(r.transform);
			foreach (Material m in r.sharedMaterials)
			{
				if (m == null)
				{
					Note(found, "no material", name);
				}
				else if (m.shader == null || !m.shader.isSupported || m.shader.name.Contains("InternalError"))
				{
					Note(found, "bad shader", name + " " + m.name + " " + ((m.shader != null) ? m.shader.name : "none"));
				}
				else if (m.HasProperty("_MainTex") && m.mainTexture == null)
				{
					Note(found, "no texture", name + " " + m.name + " " + m.shader.name);
				}
				else if (m.mainTexture != null && m.mainTexture.name.StartsWith("BlackPH"))
				{
					Note(found, "placeholder texture", name + " " + m.name);
				}
			}
			MeshFilter mf = r.GetComponent<MeshFilter>();
			if (r is MeshRenderer)
			{
				if (mf == null || mf.sharedMesh == null)
				{
					Note(found, "no mesh", name);
					continue;
				}
				bool isStale = false;
				if (mf.sharedMesh.isReadable && !stale.TryGetValue(mf.sharedMesh, out isStale))
				{
					isStale = Stale(mf.sharedMesh);
					stale[mf.sharedMesh] = isStale;
				}
				if (isStale)
				{
					Note(found, "stale mesh bounds", name + " " + mf.sharedMesh.name + " bounds " + mf.sharedMesh.bounds.size.ToString("0.0"));
				}
				if (r.lightmapIndex >= 0 && r.lightmapIndex < 254 && (r.lightmapIndex >= LightmapSettings.lightmaps.Length || LightmapSettings.lightmaps[r.lightmapIndex].lightmapColor == null))
				{
					Note(found, "bad lightmap", name + " index " + r.lightmapIndex);
				}
				if (cam != null && !r.isVisible && (cam.cullingMask & (1 << r.gameObject.layer)) != 0 && GeometryUtility.TestPlanesAABB(planes, r.bounds))
				{
					Note(found, "in view, not drawn", name + " behaviours=" + r.GetComponents<MonoBehaviour>().Length + " " + mf.sharedMesh.name);
				}
			}
		}
		Out("audit: " + total + " renderers in " + SceneManagerName());
		foreach (KeyValuePair<string, List<string>> kv in found)
		{
			Out(kv.Key + ": " + kv.Value.Count);
			for (int i = 0; i < kv.Value.Count && i < 8; i++)
			{
				Out("    " + kv.Value[i]);
			}
		}
	}

	private static string SceneManagerName()
	{
		StringBuilder names = new StringBuilder();
		for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
		{
			names.Append(" " + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
		}
		return names.ToString();
	}

	private void Pad()
	{
		RewiredInputWrapper w = RewiredInputWrapper.Instance;
		Rewired.Player player = (w != null) ? w.RewiredPlayer : null;
		Out("xinput connected=" + PCPad.Connected + " inUse=" + PCPad.InUse + " buttons=0x" + PCPad.Buttons.ToString("x") + " left=" + PCPad.LeftStick + " right=" + PCPad.RightStick + " timescale=" + Time.timeScale + " focused=" + Application.isFocused);
		foreach (string n in Input.GetJoystickNames())
		{
			Out("unity joystick '" + n + "'");
		}
		foreach (Rewired.Joystick j in Rewired.ReInput.controllers.GetJoysticks())
		{
			StringBuilder axes = new StringBuilder();
			for (int i = 0; i < j.axisCount; i++)
			{
				axes.Append(" " + j.GetAxis(i).ToString("0.00"));
			}
			StringBuilder buttons = new StringBuilder();
			for (int i = 0; i < j.buttonCount; i++)
			{
				if (j.GetButton(i))
				{
					buttons.Append(" " + i);
				}
			}
			Out("joystick " + j.id + " '" + j.name + "' hardware='" + j.hardwareName + "' guid=" + j.hardwareTypeGuid + " axes:" + axes + " buttons down:" + buttons);
			if (player != null)
			{
				Out("  owned by player: " + player.controllers.ContainsController(j));
				foreach (Rewired.ControllerMap m in player.controllers.maps.GetMaps(Rewired.ControllerType.Joystick, j.id))
				{
					Out("  map category " + m.categoryId + " enabled=" + m.enabled + " elements=" + m.elementMapCount);
				}
			}
		}
		if (player != null)
		{
			Rewired.Controller last = player.controllers.GetLastActiveController();
			Out("last active: " + ((last != null) ? last.type + " " + last.name : "none"));
			Out("camera " + player.GetAxis("CameraHorizontal").ToString("0.00") + "," + player.GetAxis("CameraVertical").ToString("0.00") + " move " + player.GetAxis("MoveHorizontal").ToString("0.00") + "," + player.GetAxis("MoveVertical").ToString("0.00") + " look " + JoystickInputHelper.CameraMovement);
			foreach (Rewired.ControllerMap m in player.controllers.maps.GetMaps(Rewired.ControllerType.Mouse, 0))
			{
				Out("mouse map category " + m.categoryId + " enabled=" + m.enabled + " elements=" + m.elementMapCount);
			}
		}
		GameplayController gc = GameplayController.Instance;
		if (gc != null && gc.cameraController != null)
		{
			Out("camera x=" + gc.cameraController.GetX().ToString("0.0") + " y=" + gc.cameraController.GetY().ToString("0.0") + " sweepAllowed=" + gc.inputController.enableCameraSweepAndPlayerMovement + " swipeSpeed=" + gc.inputController.swipeSpeed);
		}
	}

	private static string PathOf(Transform t)
	{
		string p = t.name;
		while (t.parent != null)
		{
			t = t.parent;
			p = t.name + "/" + p;
		}
		return p;
	}

	private static string Describe(Renderer r, Camera cam)
	{
		Material m = r.sharedMaterial;
		string shader = (m != null && m.shader != null) ? m.shader.name : "none";
		string tex = (m != null && m.mainTexture != null) ? m.mainTexture.name : "-";
		int queue = (m != null) ? m.renderQueue : 0;
		float dist = (cam != null) ? Vector3.Distance(cam.transform.position, r.bounds.center) : 0f;
		return PathOf(r.transform) + " [" + r.GetType().Name + "] shader=" + shader + " tex=" + tex + " queue=" + queue + " size=" + r.bounds.size.ToString("0.0") + " dist=" + dist.ToString("0.0") + " layer=" + LayerMask.LayerToName(r.gameObject.layer) + " enabled=" + r.enabled;
	}

	private static System.Collections.IEnumerator Rerender(GameObject go, Material[] materials, int lightmapIndex, Vector4 scaleOffset)
	{
		yield return null;
		MeshRenderer r = go.AddComponent<MeshRenderer>();
		r.sharedMaterials = materials;
		r.lightmapIndex = lightmapIndex;
		r.lightmapScaleOffset = scaleOffset;
	}

	private static string Mesh(Renderer r)
	{
		MeshFilter mf = r.GetComponent<MeshFilter>();
		if (mf == null || mf.sharedMesh == null)
		{
			return "";
		}
		Mesh m = mf.sharedMesh;
		return " mesh=" + m.name + " verts=" + m.vertexCount + " subs=" + m.subMeshCount + " mats=" + r.sharedMaterials.Length + " pos=" + r.transform.position.ToString("0.0") + " scale=" + r.transform.lossyScale.ToString("0.00") + " static=" + r.gameObject.isStatic;
	}

	private void Run(string line)
	{
		string[] a = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		Camera cam = PCGraphics.WorldCamera() ?? Camera.main;
		switch (a[0])
		{
		case "transparent":
		{
			int n = (a.Length > 1) ? int.Parse(a[1]) : 20;
			List<Renderer> list = new List<Renderer>();
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (r.isVisible && r.sharedMaterial != null && r.sharedMaterial.renderQueue >= 2450)
				{
					list.Add(r);
				}
			}
			list.Sort((x, y) => y.bounds.size.sqrMagnitude.CompareTo(x.bounds.size.sqrMagnitude));
			for (int i = 0; i < list.Count && i < n; i++)
			{
				Out(Describe(list[i], cam));
			}
			break;
		}
		case "find":
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (PathOf(r.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					Out(Describe(r, cam));
				}
			}
			break;
		case "hide":
		case "show":
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (PathOf(r.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					r.enabled = a[0] == "show";
					Out(a[0] + " " + PathOf(r.transform));
				}
			}
			break;
		case "set":
			PCSettings.Set(a[1], (a.Length > 2) ? string.Join(" ", a, 2, a.Length - 2) : "");
			if (cam != null)
			{
				PCCameraFx fx = cam.GetComponent<PCCameraFx>();
				if (fx != null)
				{
					fx.Rebuild();
				}
			}
			PCLighting.Apply();
			if (a[1].StartsWith("tune_probe"))
			{
				PCRendererFix.FixProbes();
			}
			Out(a[1] + " = " + PCSettings.Get(a[1], ""));
			break;
		case "lights":
			foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
			{
				Out(PathOf(l.transform) + " " + l.type + " enabled=" + l.isActiveAndEnabled + " colour=" + l.color + " intensity=" + l.intensity + " range=" + l.range + " shadows=" + l.shadows + " mode=" + l.renderMode + " cull=" + l.cullingMask + " baked=" + l.isBaked + " forward=" + l.transform.forward);
			}
			Out("ambient " + RenderSettings.ambientMode + " " + RenderSettings.ambientLight + " sky=" + RenderSettings.ambientSkyColor + " intensity=" + RenderSettings.ambientIntensity + " fog=" + RenderSettings.fog + " " + RenderSettings.fogColor + " " + RenderSettings.fogMode + " " + RenderSettings.fogStartDistance + "-" + RenderSettings.fogEndDistance + " density=" + RenderSettings.fogDensity);
			break;
		case "shot":
		{
			string file = a[1];
			if (!Path.IsPathRooted(file))
			{
				file = Path.Combine(Path.GetDirectoryName(path), file);
			}
			Application.CaptureScreenshot(file);
			Out("screenshot " + file);
			break;
		}
		case "timescale":
			Time.timeScale = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
			break;
		case "scene":
			for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
			{
				Out("scene " + UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name);
			}
			if (cam != null)
			{
				Out("camera " + PathOf(cam.transform) + " pos=" + cam.transform.position + " fov=" + cam.fieldOfView + " far=" + cam.farClipPlane + " hdr=" + cam.allowHDR + " path=" + cam.actualRenderingPath + " depth=" + cam.depthTextureMode + " occlusion=" + cam.useOcclusionCulling + " near=" + cam.nearClipPlane);
			}
			foreach (Camera c in Camera.allCameras)
			{
				Out("cam " + PathOf(c.transform) + " depth=" + c.depth + " ortho=" + c.orthographic + " mask=" + c.cullingMask + " clear=" + c.clearFlags + " target=" + (c.targetTexture != null ? c.targetTexture.name : "screen"));
			}
			Out("quality " + QualitySettings.GetQualityLevel() + " game=" + ConditionalCompile.GraphicsQuality + " vsync=" + QualitySettings.vSyncCount + " fps=" + Application.targetFrameRate + " shadows=" + QualitySettings.shadows + " " + QualitySettings.shadowDistance + " pixelLights=" + QualitySettings.pixelLightCount + " screen=" + Screen.width + "x" + Screen.height + " " + (1f / Time.smoothDeltaTime).ToString("0") + " fps now");
			break;
		case "component":
			// component <path text>: the components on matching objects
			foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
			{
				if (PathOf(t).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					StringBuilder sb = new StringBuilder(PathOf(t) + ":");
					foreach (Component c in t.GetComponents<Component>())
					{
						sb.Append(" " + (c != null ? c.GetType().Name : "missing"));
					}
					Out(sb.ToString());
				}
			}
			break;
		case "ray":
		{
			// ray <x> <y>: renderers whose bounds the view ray through that point (0..1 of the screen) passes
			if (cam == null)
			{
				break;
			}
			Ray ray = cam.ViewportPointToRay(new Vector3(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), 0f));
			List<KeyValuePair<float, Renderer>> hits = new List<KeyValuePair<float, Renderer>>();
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				float d;
				if (r.isVisible && r.bounds.IntersectRay(ray, out d))
				{
					hits.Add(new KeyValuePair<float, Renderer>(d, r));
				}
			}
			hits.Sort((x, y) => x.Key.CompareTo(y.Key));
			for (int i = 0; i < hits.Count && i < 15; i++)
			{
				Out(hits[i].Key.ToString("0.0") + " " + Describe(hits[i].Value, cam) + Mesh(hits[i].Value));
			}
			break;
		}
		case "visible":
		{
			// visible [n]: the visible renderers that cover the most of the screen
			if (cam == null)
			{
				break;
			}
			int n = (a.Length > 1) ? int.Parse(a[1]) : 20;
			List<KeyValuePair<float, Renderer>> list = new List<KeyValuePair<float, Renderer>>();
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (!r.isVisible || r.gameObject.layer == LayerMask.NameToLayer("GUI Layer"))
				{
					continue;
				}
				float d = Mathf.Max(0.5f, Vector3.Distance(cam.transform.position, r.bounds.center));
				list.Add(new KeyValuePair<float, Renderer>(r.bounds.extents.sqrMagnitude / (d * d), r));
			}
			list.Sort((x, y) => y.Key.CompareTo(x.Key));
			for (int i = 0; i < list.Count && i < n; i++)
			{
				Out(Describe(list[i].Value, cam) + Mesh(list[i].Value));
			}
			break;
		}
		case "unique":
			// unique <text>: give matching renderers their own material copies (keeps them out of batches)
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (PathOf(r.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					r.material = new Material(r.sharedMaterial);
					Out("unique " + PathOf(r.transform));
				}
			}
			break;
		case "shader":
			// shader <text> <shader name>: draw matching renderers with another shader
		{
			Shader s = Shader.Find(string.Join(" ", a, 2, a.Length - 2));
			Out("shader " + (s != null ? s.name : "not found"));
			if (s == null)
			{
				break;
			}
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (PathOf(r.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					r.material.shader = s;
					Out("  " + PathOf(r.transform));
				}
			}
			break;
		}
		case "meshinfo":
			// meshinfo <text>: the live mesh's vertices, local and in the world
			foreach (MeshFilter mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
			{
				if (PathOf(mf.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0 || mf.sharedMesh == null)
				{
					continue;
				}
				Mesh m = mf.sharedMesh;
				Vector3[] v = m.vertices;
				Vector3 lmin = Vector3.one * float.MaxValue;
				Vector3 lmax = Vector3.one * float.MinValue;
				Vector3 wmin = lmin;
				Vector3 wmax = lmax;
				foreach (Vector3 p in v)
				{
					lmin = Vector3.Min(lmin, p);
					lmax = Vector3.Max(lmax, p);
					Vector3 w = mf.transform.TransformPoint(p);
					wmin = Vector3.Min(wmin, w);
					wmax = Vector3.Max(wmax, w);
				}
				Out(PathOf(mf.transform) + " mesh=" + m.name + " id=" + m.GetInstanceID() + " verts=" + v.Length + " readable local " + lmin.ToString("0.0") + ".." + lmax.ToString("0.0") + " world " + wmin.ToString("0.0") + ".." + wmax.ToString("0.0") + " meshBounds " + m.bounds.size.ToString("0.0"));
			}
			break;
		case "remesh":
			// remesh <text>: replace matching meshes with fresh copies made from their own CPU data
			foreach (MeshFilter mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
			{
				if (PathOf(mf.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0 || mf.sharedMesh == null)
				{
					continue;
				}
				Mesh src = mf.sharedMesh;
				Mesh copy = new Mesh();
				copy.vertices = src.vertices;
				copy.uv = src.uv;
				copy.colors32 = src.colors32;
				copy.normals = src.normals;
				copy.subMeshCount = src.subMeshCount;
				for (int i = 0; i < src.subMeshCount; i++)
				{
					copy.SetTriangles(src.GetTriangles(i), i);
				}
				copy.RecalculateBounds();
				mf.sharedMesh = copy;
				Out("remesh " + PathOf(mf.transform) + " colors=" + src.colors32.Length + " uv2=" + src.uv2.Length + " tangents=" + src.tangents.Length);
			}
			break;
		case "scale":
			// scale <text> x y z: set matching objects' local scale
			foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
			{
				if (PathOf(t).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
					t.localScale = new Vector3(float.Parse(a[2], ci), float.Parse(a[3], ci), float.Parse(a[4], ci));
					Out("scale " + PathOf(t) + " " + t.localScale);
				}
			}
			break;
		case "level":
			// level <scene>: load a level the way the game's menus do (Predator_L1, Alien_L3, SA1_MarineBaseLargeRoom...)
			ActionLoadLevel.LoadScene(a[1]);
			Out("loading " + a[1]);
			break;
		case "tap":
			// tap: dismiss the level's "Tap to start" screen
			Out(PCLauncherBridge.StartLevel() ? "started" : "not ready");
			break;
		case "lm":
		{
			// lm: the scene's lightmaps, and the lightmap slots of the renderers covering the most of the screen
			LightmapData[] maps = LightmapSettings.lightmaps;
			Out("lightmaps " + maps.Length + " mode=" + LightmapSettings.lightmapsMode);
			for (int i = 0; i < maps.Length; i++)
			{
				Texture2D t = maps[i].lightmapColor;
				Out("  [" + i + "] " + (t != null ? t.name + " " + t.width + "x" + t.height + " " + t.format : "null"));
			}
			if (cam == null)
			{
				break;
			}
			List<KeyValuePair<float, Renderer>> list = new List<KeyValuePair<float, Renderer>>();
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (r.isVisible && r is MeshRenderer)
				{
					float d = Mathf.Max(0.5f, Vector3.Distance(cam.transform.position, r.bounds.center));
					list.Add(new KeyValuePair<float, Renderer>(r.bounds.extents.sqrMagnitude / (d * d), r));
				}
			}
			list.Sort((x, y) => y.Key.CompareTo(x.Key));
			for (int i = 0; i < list.Count && i < 12; i++)
			{
				Renderer r = list[i].Value;
				Material m = r.sharedMaterial;
				Out("  " + PathOf(r.transform) + " lm=" + r.lightmapIndex + " " + r.lightmapScaleOffset.ToString("0.000") + " shader=" + (m != null ? m.shader.name : "-") + " tex=" + (m != null && m.mainTexture != null ? m.mainTexture.name : "-") + " color=" + (m != null && m.HasProperty("_Color") ? m.color.ToString() : "-"));
			}
			break;
		}
		case "flip":
			// flip <text>: reverse the triangle winding of matching meshes (copies)
			foreach (MeshFilter mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
			{
				if (PathOf(mf.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0 || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
				{
					continue;
				}
				Mesh copy = UnityEngine.Object.Instantiate(mf.sharedMesh);
				for (int s = 0; s < copy.subMeshCount; s++)
				{
					int[] tri = copy.GetTriangles(s);
					for (int i = 0; i + 2 < tri.Length; i += 3)
					{
						int k = tri[i + 1];
						tri[i + 1] = tri[i + 2];
						tri[i + 2] = k;
					}
					copy.SetTriangles(tri, s);
				}
				mf.sharedMesh = copy;
				Out("flipped " + PathOf(mf.transform) + " det=" + mf.transform.localToWorldMatrix.determinant.ToString("0.00"));
			}
			break;
		case "fog":
			// fog on|off | fog density <x> | fog linear <start> <end>
			if (a[1] == "on" || a[1] == "off")
			{
				RenderSettings.fog = a[1] == "on";
			}
			else if (a[1] == "density")
			{
				RenderSettings.fogDensity = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
			}
			else if (a[1] == "linear")
			{
				RenderSettings.fogMode = FogMode.Linear;
				RenderSettings.fogStartDistance = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
				RenderSettings.fogEndDistance = float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture);
			}
			Out("fog " + RenderSettings.fog + " " + RenderSettings.fogMode + " density=" + RenderSettings.fogDensity + " " + RenderSettings.fogStartDistance + "-" + RenderSettings.fogEndDistance);
			break;
		case "occlusion":
			// occlusion on|off: the world camera's occlusion culling
			if (cam != null)
			{
				cam.useOcclusionCulling = a[1] == "on";
				Out("occlusion " + cam.useOcclusionCulling);
			}
			break;
		case "freecam":
		{
			// freecam x y z pitch yaw [fov] | freecam off | freecam here pitch yaw: a debug camera drawn over the game's
			GameObject fc = GameObject.Find("PCPort freecam");
			if (a[1] == "off")
			{
				if (fc != null)
				{
					UnityEngine.Object.Destroy(fc);
				}
				break;
			}
			if (fc == null)
			{
				fc = new GameObject("PCPort freecam");
				Camera fcam = fc.AddComponent<Camera>();
				fcam.depth = 40f;
				fcam.farClipPlane = 1000f;
				fcam.nearClipPlane = 0.1f;
				fcam.useOcclusionCulling = false;
				if (cam != null)
				{
					fcam.cullingMask = cam.cullingMask;
					fcam.clearFlags = cam.clearFlags;
					fcam.backgroundColor = cam.backgroundColor;
				}
			}
			System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;
			int k = 1;
			Vector3 pos;
			if (a[1] == "here" && cam != null)
			{
				pos = cam.transform.position;
				k = 2;
			}
			else
			{
				pos = new Vector3(float.Parse(a[1], ci), float.Parse(a[2], ci), float.Parse(a[3], ci));
				k = 4;
			}
			fc.transform.position = pos;
			fc.transform.rotation = Quaternion.Euler(float.Parse(a[k], ci), float.Parse(a[k + 1], ci), 0f);
			fc.GetComponent<Camera>().fieldOfView = (a.Length > k + 2) ? float.Parse(a[k + 2], ci) : 60f;
			Out("freecam at " + pos + " pitch " + a[k] + " yaw " + a[k + 1]);
			break;
		}
		case "clone":
			// clone <text>: a new object with the same mesh, material and place, drawn with DebugUnlit
			foreach (MeshFilter mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
			{
				if (PathOf(mf.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0 || mf.sharedMesh == null || mf.name.StartsWith("PCPort clone"))
				{
					continue;
				}
				GameObject go = new GameObject("PCPort clone " + mf.name);
				go.transform.position = mf.transform.position;
				go.transform.rotation = mf.transform.rotation;
				go.transform.localScale = mf.transform.lossyScale;
				go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
				MeshRenderer src = mf.GetComponent<MeshRenderer>();
				Material m = new Material(Shader.Find("Hidden/PCPort/DebugUnlit"));
				if (src != null && src.sharedMaterial != null)
				{
					m.mainTexture = src.sharedMaterial.mainTexture;
				}
				go.AddComponent<MeshRenderer>().sharedMaterial = m;
				Out("cloned " + PathOf(mf.transform) + " layer " + mf.gameObject.layer + " active " + mf.gameObject.activeInHierarchy);
			}
			break;
		case "lmindex":
			// lmindex <text> <n>: set matching renderers' lightmap index (-1 for none)
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (PathOf(r.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					r.lightmapIndex = int.Parse(a[2]);
					Out("lightmapIndex " + r.lightmapIndex + " " + PathOf(r.transform) + " realtimeIndex=" + r.realtimeLightmapIndex + " probes=" + r.lightProbeUsage + " shadows=" + r.shadowCastingMode + " motion=" + r.motionVectorGenerationMode);
				}
			}
			break;
		case "unparent":
			foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
			{
				if (PathOf(t).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0 && t.GetComponent<Renderer>() != null)
				{
					t.SetParent(null, true);
					Out("unparented " + t.name);
				}
			}
			break;
		case "animinfo":
			foreach (Animation an in UnityEngine.Object.FindObjectsOfType<Animation>())
			{
				if (PathOf(an.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				StringBuilder sb = new StringBuilder(PathOf(an.transform) + " playing=" + an.isPlaying + " culling=" + an.cullingType + " enabled=" + an.enabled + " clip=" + (an.clip != null ? an.clip.name : "none") + " clips:");
				foreach (AnimationState st in an)
				{
					sb.Append(" " + st.name + "(" + st.length.ToString("0.0") + "s t=" + st.time.ToString("0.0") + " w=" + st.weight + " " + st.enabled + ")");
				}
				Out(sb.ToString());
			}
			break;
		case "rerender":
			// rerender <text>: replace matching MeshRenderers with new ones (same materials and lightmap)
			foreach (MeshRenderer r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
			{
				if (PathOf(r.transform).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0)
				{
					StartCoroutine(Rerender(r.gameObject, r.sharedMaterials, r.lightmapIndex, r.lightmapScaleOffset));
					UnityEngine.Object.Destroy(r);
					Out("rerender " + PathOf(r.transform));
				}
			}
			break;
		case "probes":
		{
			// probes: the light probe data and the light the camera's position would get from it
			LightProbes lp = LightmapSettings.lightProbes;
			Out("light probes: " + (lp != null ? lp.count + " probes, " + lp.cellCount + " cells" : "none"));
			if (lp != null && cam != null)
			{
				UnityEngine.Rendering.SphericalHarmonicsL2 sh;
				LightProbes.GetInterpolatedProbe(cam.transform.position, null, out sh);
				Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.forward };
				Color[] results = new Color[3];
				sh.Evaluate(dirs, results);
				Out("  at camera: up " + results[0] + " down " + results[1] + " forward " + results[2] + " dc " + sh[0, 0] + "," + sh[1, 0] + "," + sh[2, 0]);
				UnityEngine.Rendering.SphericalHarmonicsL2[] all = lp.bakedProbes;
				int bad = 0;
				float max = 0f;
				for (int i = 0; i < all.Length; i++)
				{
					for (int c = 0; c < 3; c++)
					{
						for (int k = 0; k < 9; k++)
						{
							float v = all[i][c, k];
							if (float.IsNaN(v) || float.IsInfinity(v))
							{
								bad++;
							}
							else
							{
								max = Mathf.Max(max, Mathf.Abs(v));
							}
						}
					}
				}
				Out("  NaN/infinite coefficients " + bad + ", largest " + max);
			}
			break;
		}
		case "mirrored":
			// mirrored: renderers whose transform still mirrors them
			foreach (Renderer r in UnityEngine.Object.FindObjectsOfType<Renderer>())
			{
				if (r.transform.localToWorldMatrix.determinant < 0f)
				{
					Out(Describe(r, cam) + Mesh(r) + " children=" + r.transform.childCount + " anim=" + (r.GetComponentInParent<Animation>() != null) + "/" + (r.GetComponentInParent<Animator>() != null));
				}
			}
			break;
		case "mirrorfix":
			Out("rewrote " + PCMirrorFix.Apply() + " transforms");
			break;
		case "xform":
			// xform <text>: the transform chain of matching objects (local values, then each parent)
			foreach (Transform t in UnityEngine.Object.FindObjectsOfType<Transform>())
			{
				if (PathOf(t).IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				Out(PathOf(t));
				for (Transform p = t; p != null; p = p.parent)
				{
					Out("   " + p.name + " pos=" + p.localPosition.ToString("0.000") + " rot=" + p.localRotation.ToString("0.0000") + " euler=" + p.localEulerAngles.ToString("0.0") + " scale=" + p.localScale.ToString("0.000"));
				}
				Renderer r = t.GetComponent<Renderer>();
				if (r != null)
				{
					Out("   bounds " + r.bounds.center.ToString("0.0") + " size " + r.bounds.size.ToString("0.0"));
				}
			}
			break;
		case "pad":
			Pad();
			break;
		case "audit":
			Audit(cam);
			break;
		case "rumble":
			if (a[1] == "strike")
			{
				PCPad.RumbleStrike(false);
			}
			else if (a[1] == "hurt")
			{
				PCPad.RumbleHurt(true);
			}
			else
			{
				PCPad.RumbleSlice();
			}
			Out("rumble " + a[1] + " inUse=" + PCPad.InUse);
			break;
		case "controls":
			UnityEngine.Object.FindObjectOfType<PCControls>().Toggle();
			break;
		case "tip":
			GameplayController.Instance.inputController.pauseMenuController.ShowSimpleToolTip(PCStrings.Symbols(line.Substring(4)));
			break;
		case "fx":
			// fx: the world camera's components; fx <type> on|off: turn one on or off
			if (cam == null)
			{
				break;
			}
			foreach (Behaviour b in cam.GetComponents<Behaviour>())
			{
				if (a.Length > 2 && b.GetType().Name.Equals(a[1], StringComparison.OrdinalIgnoreCase))
				{
					b.enabled = a[2] == "on";
				}
				Out(b.GetType().Name + " enabled=" + b.enabled);
			}
			break;
		default:
			Out("unknown command");
			break;
		}
	}
}
