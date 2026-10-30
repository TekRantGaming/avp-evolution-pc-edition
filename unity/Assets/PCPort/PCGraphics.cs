using System;
using UnityEngine;
using UnityEngine.PostProcessing;

// PC port: the launcher's graphics options on the camera that draws the world. Finds that camera every frame (the
// gameplay camera in a level, Camera.main elsewhere) and gives it a PCCameraFx.
public class PCGraphics : MonoBehaviour
{
	private Camera current;

	public static Camera WorldCamera()
	{
		GameplayController gc = GameplayController.Instance;
		if (gc != null && gc.cameraController != null && gc.cameraController.gameplayCamera != null && gc.cameraController.gameplayCamera.isActiveAndEnabled)
		{
			return gc.cameraController.gameplayCamera;
		}
		Camera main = Camera.main;
		if (main != null && !main.orthographic)
		{
			return main;
		}
		return null;
	}

	private void LateUpdate()
	{
		Camera cam = WorldCamera();
		if (cam == current)
		{
			return;
		}
		current = cam;
		if (cam != null && cam.GetComponent<PCCameraFx>() == null)
		{
			cam.gameObject.AddComponent<PCCameraFx>();
		}
	}
}

public class PCCameraFx : MonoBehaviour
{
	private Camera cam;

	private PostProcessingProfile profile;

	private PostProcessingBehaviour post;

	private Smaa.SMAA smaa;

	private PCLightShafts shafts;

	private float gameFov;

	private float gameFar;

	private bool restore;

	private float nextRefresh;

	private int campaign = -1;

	// Upscaler: the world is drawn into lowRes, and PCUpscaleBlit's camera (just after this one, before the HUD's
	// cameras) scales it to the screen.
	private RenderTexture lowRes;

	private Camera upscaleCamera;

	private string upscaler = "off";

	private float renderScale = 1f;

	// DLSS: the camera's projection is jittered every frame and PCDlssCapture copies its depth and motion vectors.
	private int dlssQuality = 2;

	private PCDlssCapture capture;

	private int jitterIndex;

	private Vector2 jitter;

	private bool jittered;

	private Vector3 lastCameraPosition;

	private void Start()
	{
		cam = GetComponent<Camera>();
		Setup();
	}

	// PCConsole's "set": the settings changed, so the effects are made again.
	public void Rebuild()
	{
		Teardown();
		Setup();
	}

	private void Teardown()
	{
		if (smaa != null)
		{
			UnityEngine.Object.DestroyImmediate(smaa);
		}
		if (shafts != null)
		{
			UnityEngine.Object.DestroyImmediate(shafts);
		}
		if (post != null)
		{
			UnityEngine.Object.DestroyImmediate(post);
		}
		if (capture != null)
		{
			UnityEngine.Object.DestroyImmediate(capture);
		}
		jitterIndex = 0;
		if (profile != null)
		{
			UnityEngine.Object.Destroy(profile);
		}
		if (cam.targetTexture == lowRes)
		{
			cam.targetTexture = null;
		}
		if (upscaleCamera != null)
		{
			upscaleCamera.enabled = false;
		}
		campaign = -1;
		renderScale = 1f;
	}

	private void Setup()
	{
		// The levels' occlusion data was baked for the phone build's batched scenes and hides walls that should be
		// seen (Alien Mission 1's hive); a PC draws these levels easily without it.
		cam.useOcclusionCulling = PCSettings.GetBool("tune_occlusion", false);
		upscaler = PCSettings.Get("avp_upscaler", "off");
		if (upscaler != "off")
		{
			switch (PCSettings.Get("avp_upscaler_quality", "quality"))
			{
			case "native":
				renderScale = 1f;  // FSR 1 then only sharpens; DLSS and XeSS anti-alias (DLAA)
				break;
			case "ultra":
				renderScale = 1f / 1.3f;
				break;
			case "balanced":
				renderScale = 1f / 1.7f;
				break;
			case "performance":
				renderScale = 0.5f;
				break;
			default:
				renderScale = 1f / 1.5f;
				break;
			}
			if (upscaler == "dlss" || upscaler == "xess")
			{
				PCDlss.Kind = (upscaler == "xess") ? PCDlss.KindXess : PCDlss.KindDlss;
				upscaler = "dlss";  // both run through the native plugin
				dlssQuality = PCDlss.QualityFor(PCSettings.Get("avp_upscaler_quality", "quality"));
				PCDlss.Init();
			}
			else if (upscaler != "fsr1")
			{
				Debug.Log("PCGraphics: upscaler " + upscaler + " is not available in this build; using AMD FSR 1");
				upscaler = "fsr1";
			}
		}
		bool hdr = PCSettings.GetBool("avp_bloom", true) || PCSettings.Get("avp_tonemap", "aces") == "aces";
		cam.allowHDR = hdr;
		string aa = PCSettings.Get("avp_aa", "smaa");
		if (upscaler == "dlss")
		{
			aa = "off";  // DLSS does its own anti-aliasing
		}
		if (aa == "smaa")
		{
			smaa = gameObject.AddComponent<Smaa.SMAA>();
			smaa.Shader = Shader.Find("Hidden/Subpixel Morphological Antialiasing");
			smaa.Quality = Smaa.QualityPreset.High;
			smaa.Hdr = hdr ? Smaa.HDRMode.On : Smaa.HDRMode.Off;
		}
		if (PCSettings.GetBool("avp_light_shafts", true))
		{
			shafts = gameObject.AddComponent<PCLightShafts>();
			shafts.intensity = Tune("shafts", shafts.intensity);
			shafts.threshold = Tune("shafts_threshold", shafts.threshold);
		}
		profile = ScriptableObject.CreateInstance<PostProcessingProfile>();
		BuildProfile(aa);
		post = gameObject.AddComponent<PostProcessingBehaviour>();
		post.profile = profile;
		if (upscaler == "dlss")
		{
			capture = gameObject.AddComponent<PCDlssCapture>();
		}
		Debug.Log("PCGraphics: " + cam.name + " aa " + aa + ", upscaler " + upscaler + " x" + renderScale.ToString("0.00") + ", hdr " + hdr);
		Refresh();
	}

	// Tuning values (PCConsole: set tune_<name> <value>).
	private static float Tune(string name, float def)
	{
		return PCSettings.GetFloat("tune_" + name, def);
	}

	private bool Gameplay
	{
		get
		{
			GameplayController gc = GameplayController.Instance;
			return gc != null && gc.cameraController != null && gc.cameraController.gameplayCamera == cam;
		}
	}

	private void BuildProfile(string aa)
	{
		AntialiasingModel.Settings a = profile.antialiasing.settings;
		if (aa == "fxaa")
		{
			a.method = AntialiasingModel.Method.Fxaa;
			a.fxaaSettings.preset = AntialiasingModel.FxaaPreset.ExtremeQuality;
			profile.antialiasing.enabled = true;
		}
		else if (aa == "taa")
		{
			a.method = AntialiasingModel.Method.Taa;
			profile.antialiasing.enabled = true;
		}
		profile.antialiasing.settings = a;

		string ssao = PCSettings.Get("avp_ssao", "low");
		if (ssao != "off")
		{
			AmbientOcclusionModel.Settings o = profile.ambientOcclusion.settings;
			bool high = ssao == "high";
			o.intensity = Tune("ao_intensity", high ? 1.1f : 0.9f);
			o.radius = Tune("ao_radius", high ? 0.35f : 0.25f);
			o.sampleCount = high ? AmbientOcclusionModel.SampleCount.Medium : AmbientOcclusionModel.SampleCount.Low;
			o.downsampling = !high;
			o.forceForwardCompatibility = true;
			o.highPrecision = high;
			o.ambientOnly = false;
			profile.ambientOcclusion.settings = o;
			profile.ambientOcclusion.enabled = true;
		}

		if (PCSettings.GetBool("avp_bloom", true))
		{
			BloomModel.Settings b = profile.bloom.settings;
			b.bloom.intensity = Tune("bloom", 0.12f);
			b.bloom.threshold = Tune("bloom_threshold", 2.5f);
			b.bloom.softKnee = 0.5f;
			b.bloom.radius = Tune("bloom_radius", 3f);
			b.bloom.antiFlicker = true;
			profile.bloom.settings = b;
			profile.bloom.enabled = true;
		}
		profile.dithering.enabled = true;

		// Auto exposure (with ACES): the darkest levels open up a little and bright ones stay as they are; the limits
		// keep it from turning night scenes into day.
		if (PCSettings.Get("avp_tonemap", "aces") == "aces" && PCSettings.GetBool("avp_auto_exposure", true) && SystemInfo.supportsComputeShaders)
		{
			EyeAdaptationModel.Settings e = profile.eyeAdaptation.settings;
			e.minLuminance = Tune("eye_min", -4f);
			e.maxLuminance = Tune("eye_max", 0.5f);
			e.keyValue = Tune("eye_key", 0.18f);
			e.dynamicKeyValue = false;
			e.adaptationType = EyeAdaptationModel.EyeAdaptationType.Progressive;
			e.speedUp = 1.5f;
			e.speedDown = 1f;
			profile.eyeAdaptation.settings = e;
			profile.eyeAdaptation.enabled = true;
		}
		ApplyGrading(0);
	}

	// 0: menus, 1: Alien campaign, 2: Predator campaign.
	private void ApplyGrading(int which)
	{
		campaign = which;
		bool aces = PCSettings.Get("avp_tonemap", "aces") == "aces";
		bool grading = PCSettings.GetBool("avp_grading", true);
		ColorGradingModel.Settings g = profile.colorGrading.settings;
		g.tonemapping.tonemapper = aces ? ColorGradingModel.Tonemapper.ACES : ColorGradingModel.Tonemapper.None;
		g.basic.postExposure = Tune("exposure", aces ? 0.9f : 0f);
		g.basic.contrast = 1f;
		g.basic.saturation = 1f;
		g.basic.temperature = 0f;
		g.basic.tint = 0f;
		g.colorWheels.mode = ColorGradingModel.ColorWheelMode.Linear;
		g.colorWheels.linear.lift = Color.clear;
		g.colorWheels.linear.gamma = Color.clear;
		g.colorWheels.linear.gain = Color.clear;
		if (grading)
		{
			// Gentle on purpose: the game renders in gamma space, where PPv1's contrast and lift crush the shadows.
			string side = (which == 1) ? "alien" : ((which == 2) ? "pred" : "menu");
			float[] d = (which == 1) ? new float[] { -8f, -4f, 0.97f, 1f, 0f, 0f } : ((which == 2) ? new float[] { 7f, 1f, 1.04f, 1f, 0f, 0.02f } : new float[] { 0f, 0f, 1.03f, 1f, 0f, 0f });
			g.basic.temperature = Tune(side + "_temp", d[0]);
			g.basic.tint = Tune(side + "_tint", d[1]);
			g.basic.saturation = Tune(side + "_sat", d[2]);
			g.basic.contrast = Tune(side + "_contrast", d[3]);
			g.colorWheels.linear.lift = new Color(1f, 1f, 1f, Tune(side + "_lift", d[4]));
			g.colorWheels.linear.gain = (which == 2) ? new Color(1f, 0.92f, 0.8f, Tune(side + "_gain", d[5])) : ((which == 1) ? new Color(0.9f, 1f, 1f, Tune(side + "_gain", d[5])) : new Color(1f, 1f, 1f, Tune(side + "_gain", d[5])));
		}
		profile.colorGrading.settings = g;
		profile.colorGrading.enabled = aces || grading;
	}

	private void Update()
	{
		if (Time.unscaledTime >= nextRefresh)
		{
			nextRefresh = Time.unscaledTime + 1f;
			Refresh();
		}
		UpdateUpscaler();
	}

	// Settings the game puts back on its own (its quality options, level start), applied again every second.
	private void Refresh()
	{
		GameplayController gc = GameplayController.Instance;
		int which = 0;
		if (gc != null && Gameplay)
		{
			which = (gc.WhatToSpawn == PlayerType.Alien) ? 1 : 2;
			ApplyShadows(gc.cameraController);
			if (shafts != null)
			{
				shafts.sun = (gc.cameraController.directionalLight != null) ? gc.cameraController.directionalLight : gc.cameraController.directionalLightShadows;
			}
		}
		if (which != campaign)
		{
			ApplyGrading(which);
		}
	}

	private static void ApplyShadows(MainCamera mc)
	{
		string shadows = PCSettings.Get("avp_shadows", "high");
		if (shadows == "off" || mc.directionalLightShadows == null || ConditionalCompile.GraphicsQuality == DevicePerformance.Low)
		{
			return;
		}
		Light light = mc.directionalLightShadows;
		light.enabled = true;
		light.shadows = (shadows == "low") ? LightShadows.Hard : LightShadows.Soft;
		light.shadowStrength = 0.75f;
		light.shadowBias = 0.03f;
		light.shadowNormalBias = 0.3f;
		light.cullingMask |= LayerMask.GetMask("Environment", "Enemies", "Player");
		switch (shadows)
		{
		case "low":
			QualitySettings.shadowResolution = ShadowResolution.Medium;
			QualitySettings.shadowDistance = 30f;
			QualitySettings.shadowCascades = 1;
			break;
		case "ultra":
			QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
			QualitySettings.shadowDistance = 80f;
			QualitySettings.shadowCascades = 4;
			break;
		default:
			QualitySettings.shadowResolution = ShadowResolution.High;
			QualitySettings.shadowDistance = 50f;
			QualitySettings.shadowCascades = 2;
			break;
		}
		QualitySettings.shadowProjection = ShadowProjection.StableFit;
		QualitySettings.shadows = ShadowQuality.All;
	}

	// ------------------------------------------------------------- per frame view

	private void OnPreCull()
	{
		if (cam == null)
		{
			return;
		}
		restore = true;
		gameFov = cam.fieldOfView;
		gameFar = cam.farClipPlane;
		if (Gameplay)
		{
			float fov = Mathf.Clamp(PCSettings.GetFloat("avp_fov", 65f), 50f, 100f);
			float k = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(PCSettings.GameFov * 0.5f * Mathf.Deg2Rad);
			cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(gameFov * 0.5f * Mathf.Deg2Rad) * k) * Mathf.Rad2Deg, 1f, 150f);
		}
		string draw = PCSettings.Get("avp_draw_distance", "far");
		if (draw != "original")
		{
			cam.farClipPlane = gameFar * ((draw == "max") ? 3f : 1.75f);
		}
		if (DlssActive && lowRes != null)
		{
			// Halton (2, 3) sub-pixel offsets, as many as DLSS wants for this scale.
			int phases = Mathf.Max(8, Mathf.CeilToInt(8f * Mathf.Pow((float)Screen.width / lowRes.width, 2f)));
			jitterIndex = (jitterIndex + 1) % phases;
			jitter = new Vector2(Halton(jitterIndex + 1, 2) - 0.5f, Halton(jitterIndex + 1, 3) - 0.5f);
			cam.ResetProjectionMatrix();
			Matrix4x4 p = cam.projectionMatrix;
			cam.nonJitteredProjectionMatrix = p;
			p.m02 += jitter.x * 2f / lowRes.width;
			p.m12 += jitter.y * 2f / lowRes.height;
			cam.projectionMatrix = p;
			jittered = true;
			if (capture != null)
			{
				capture.jitter = jitter;
			}
		}
	}

	private static float Halton(int index, int radix)
	{
		float result = 0f;
		float f = 1f / radix;
		while (index > 0)
		{
			result += f * (index % radix);
			index /= radix;
			f /= radix;
		}
		return result;
	}

	private bool DlssActive
	{
		get
		{
			return upscaler == "dlss" && PCDlss.Ready;
		}
	}

	private void OnPostRender()
	{
		if (restore)
		{
			restore = false;
			cam.fieldOfView = gameFov;
			cam.farClipPlane = gameFar;
		}
		if (jittered)
		{
			jittered = false;
			cam.ResetProjectionMatrix();
		}
	}

	// ----------------------------------------------------------------- upscaler

	private void UpdateUpscaler()
	{
		if (upscaler == "off")
		{
			return;
		}
		if (upscaler == "dlss" && PCDlss.Failed)
		{
			Debug.Log("PCGraphics: " + PCDlss.Name + " is not available (" + PCDlss.StatusText + "); using AMD FSR 1");
			upscaler = "fsr1";
			if (capture != null)
			{
				UnityEngine.Object.Destroy(capture);
			}
		}
		int w = Mathf.Max(64, Mathf.RoundToInt(Screen.width * renderScale));
		int h = Mathf.Max(64, Mathf.RoundToInt(Screen.height * renderScale));
		if (DlssActive)
		{
			int dw;
			int dh;
			if (PCDlss.OptimalSize(dlssQuality, Screen.width, Screen.height, out dw, out dh))
			{
				w = dw;
				h = dh;
			}
		}
		if (lowRes == null || lowRes.width != w || lowRes.height != h)
		{
			if (lowRes != null)
			{
				cam.targetTexture = null;
				lowRes.Release();
				UnityEngine.Object.Destroy(lowRes);
			}
			lowRes = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
			lowRes.name = "PCPort low resolution world";
			lowRes.filterMode = FilterMode.Bilinear;
			lowRes.wrapMode = TextureWrapMode.Clamp;
			lowRes.Create();
		}
		if (cam.targetTexture != lowRes)
		{
			cam.targetTexture = lowRes;
		}
		if (upscaleCamera == null)
		{
			GameObject go = new GameObject("PCPort upscale");
			go.transform.SetParent(transform, false);
			upscaleCamera = go.AddComponent<Camera>();
			upscaleCamera.clearFlags = CameraClearFlags.Nothing;
			upscaleCamera.cullingMask = 0;
			upscaleCamera.orthographic = true;
			upscaleCamera.useOcclusionCulling = false;
			upscaleCamera.allowHDR = false;
			upscaleCamera.allowMSAA = false;
			go.AddComponent<PCUpscaleBlit>();
		}
		upscaleCamera.depth = cam.depth + 0.001f;
		upscaleCamera.enabled = cam.enabled;
		PCUpscaleBlit blit = upscaleCamera.GetComponent<PCUpscaleBlit>();
		blit.source = lowRes;
		blit.sharpness = PCSettings.GetInt("avp_sharpness", 40);
		blit.dlss = DlssActive ? capture : null;
		if (blit.dlss != null)
		{
			blit.dlss.Prepare(lowRes, dlssQuality);
			// A cut (the camera jumping) starts DLSS's history again.
			if ((cam.transform.position - lastCameraPosition).sqrMagnitude > 25f)
			{
				blit.dlss.reset = true;
			}
		}
		lastCameraPosition = cam.transform.position;
	}

	private void OnDisable()
	{
		if (upscaleCamera != null)
		{
			upscaleCamera.enabled = false;
		}
	}

	private void OnDestroy()
	{
		if (cam != null && cam.targetTexture == lowRes)
		{
			cam.targetTexture = null;
		}
		if (lowRes != null)
		{
			lowRes.Release();
			UnityEngine.Object.Destroy(lowRes);
		}
		if (upscaleCamera != null)
		{
			UnityEngine.Object.Destroy(upscaleCamera.gameObject);
		}
		if (profile != null)
		{
			UnityEngine.Object.Destroy(profile);
		}
	}
}

// Draws PCCameraFx's low resolution world on the screen with AMD FSR 1 (EASU, then RCAS sharpening).
public class PCUpscaleBlit : MonoBehaviour
{
	public RenderTexture source;

	public int sharpness = 40;

	public PCDlssCapture dlss;

	private Material material;

	private bool fsr;

	private void OnRenderImage(RenderTexture src, RenderTexture dst)
	{
		if (source == null)
		{
			Graphics.Blit(src, dst);
			return;
		}
		if (material == null)
		{
			Shader shader = Shader.Find("Hidden/PCPort/FSR1");
			if (shader == null)
			{
				Graphics.Blit(source, dst);
				return;
			}
			material = new Material(shader);
			material.hideFlags = HideFlags.HideAndDontSave;
			fsr = shader.isSupported && SystemInfo.graphicsShaderLevel >= 50;
			Debug.Log("PCGraphics: FSR 1 " + (fsr ? "on" : "not supported, bilinear"));
		}
		int w = (dst != null) ? dst.width : Screen.width;
		int h = (dst != null) ? dst.height : Screen.height;
		if (dlss != null)
		{
			RenderTexture output = dlss.Evaluate(w, h);
			if (output != null)
			{
				if (fsr && sharpness > 0)
				{
					material.SetFloat("_FsrSharpness", (100 - Mathf.Clamp(sharpness, 0, 100)) / 50f);
					Graphics.Blit(output, dst, material, 1);
				}
				else
				{
					Graphics.Blit(output, dst);
				}
				return;
			}
		}
		if (!fsr)
		{
			Graphics.Blit(source, dst, material, 2);
			return;
		}
		material.SetVector("_FsrInput", new Vector4(source.width, source.height, source.width, source.height));
		material.SetVector("_FsrOutput", new Vector4(w, h, 0f, 0f));
		RenderTexture up = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
		up.filterMode = FilterMode.Point;
		Graphics.Blit(source, up, material, 0);
		// The launcher's 0 to 100 sharpening: 100 is RCAS at its strongest (0 stops), 0 is 2 stops softer.
		material.SetFloat("_FsrSharpness", (100 - Mathf.Clamp(sharpness, 0, 100)) / 50f);
		Graphics.Blit(up, dst, material, 1);
		RenderTexture.ReleaseTemporary(up);
	}

	private void OnDestroy()
	{
		if (material != null)
		{
			UnityEngine.Object.Destroy(material);
		}
	}
}
