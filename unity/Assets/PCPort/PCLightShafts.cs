using UnityEngine;

// PC port: light shafts from the level's main directional light (the launcher's avp_light_shafts). PCCameraFx sets
// the light; nothing is drawn while it is behind the camera.
public class PCLightShafts : MonoBehaviour
{
	public Light sun;

	public float intensity = 0.65f;

	public float threshold = 0.55f;

	private Material material;

	private Camera cam;

	private void OnEnable()
	{
		cam = GetComponent<Camera>();
		cam.depthTextureMode |= DepthTextureMode.Depth;
	}

	private void OnRenderImage(RenderTexture src, RenderTexture dst)
	{
		if (sun == null || !sun.isActiveAndEnabled || sun.type != LightType.Directional)
		{
			Graphics.Blit(src, dst);
			return;
		}
		Vector3 far = cam.transform.position - sun.transform.forward * cam.farClipPlane * 0.9f;
		Vector3 vp = cam.WorldToViewportPoint(far);
		float facing = Vector3.Dot(cam.transform.forward, -sun.transform.forward);
		float edge = Mathf.Max(Mathf.Abs(vp.x - 0.5f), Mathf.Abs(vp.y - 0.5f));
		float fade = Mathf.Clamp01(facing * 2f) * Mathf.Clamp01((1.2f - edge) * 1.5f);
		if (vp.z <= 0f || fade <= 0.01f)
		{
			Graphics.Blit(src, dst);
			return;
		}
		if (material == null)
		{
			Shader shader = Shader.Find("Hidden/PCPort/LightShafts");
			if (shader == null || !shader.isSupported)
			{
				enabled = false;
				Graphics.Blit(src, dst);
				return;
			}
			material = new Material(shader);
			material.hideFlags = HideFlags.HideAndDontSave;
		}
		Color c = sun.color * Mathf.Clamp(sun.intensity, 0.5f, 1.5f) * intensity;
		material.SetVector("_SunColor", new Vector4(c.r, c.g, c.b, 1f));
		material.SetVector("_ShaftParams", new Vector4(threshold, (cam.depthTextureMode & DepthTextureMode.Depth) != 0 ? 1f : 0f, 0.93f, 0f));
		int w = Mathf.Max(32, src.width / 4);
		int h = Mathf.Max(32, src.height / 4);
		RenderTexture a = RenderTexture.GetTemporary(w, h, 0, src.format);
		RenderTexture b = RenderTexture.GetTemporary(w, h, 0, src.format);
		material.SetVector("_SunPos", new Vector4(vp.x, vp.y, fade, 1f));
		Graphics.Blit(src, a, material, 0);
		float length = 0.35f;
		for (int i = 0; i < 3; i++)
		{
			material.SetVector("_SunPos", new Vector4(vp.x, vp.y, fade, length));
			Graphics.Blit(a, b, material, 1);
			RenderTexture t = a;
			a = b;
			b = t;
			length *= 0.5f;
		}
		material.SetTexture("_ShaftsTex", a);
		material.SetVector("_SunPos", new Vector4(vp.x, vp.y, fade, 1f));
		Graphics.Blit(src, dst, material, 2);
		RenderTexture.ReleaseTemporary(a);
		RenderTexture.ReleaseTemporary(b);
	}

	private void OnDestroy()
	{
		if (material != null)
		{
			Object.Destroy(material);
		}
	}
}
