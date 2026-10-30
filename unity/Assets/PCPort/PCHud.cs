using System.Collections.Generic;
using UnityEngine;

// PC port: the launcher's HUD options for the in-level HUD (NGUI anchors under the HUD camera).
//  - avp_hud_edges false: on screens wider than 16:9 the HUD camera keeps to the middle 16:9 area, so the health bar
//    and icons stay near the action instead of the far corners of an ultrawide screen.
//  - avp_hud_scale (50 to 150 %): every anchored group is scaled from its anchor.
public class PCHud : MonoBehaviour
{
	private readonly Dictionary<Transform, Vector3> baseScales = new Dictionary<Transform, Vector3>();

	private HUDElements hud;

	private Camera hudCamera;

	private float next;

	private void LateUpdate()
	{
		if (Time.unscaledTime < next)
		{
			return;
		}
		next = Time.unscaledTime + 0.5f;
		GameplayController gc = GameplayController.Instance;
		HUDElements current = (gc != null && gc.inputController != null) ? gc.inputController.HUD : null;
		if (current == null)
		{
			hud = null;
			return;
		}
		if (current != hud)
		{
			hud = current;
			baseScales.Clear();
			hudCamera = NGUITools.FindCameraForLayer(hud.gameObject.layer);
		}
		if (hudCamera != null)
		{
			float aspect = (float)Screen.width / Screen.height;
			const float wide = 16f / 9f;
			Rect rect = new Rect(0f, 0f, 1f, 1f);
			if (!PCSettings.GetBool("avp_hud_edges", true) && aspect > wide + 0.01f)
			{
				float w = wide / aspect;
				rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
			}
			if (hudCamera.rect != rect)
			{
				hudCamera.rect = rect;
			}
		}
		float scale = Mathf.Clamp(PCSettings.GetInt("avp_hud_scale", 100), 50, 150) / 100f;
		foreach (UIAnchor anchor in hud.GetComponentsInChildren<UIAnchor>(true))
		{
			Transform t = anchor.transform;
			Vector3 baseScale;
			if (!baseScales.TryGetValue(t, out baseScale))
			{
				baseScale = t.localScale;
				baseScales[t] = baseScale;
			}
			Vector3 want = baseScale * scale;
			if (t.localScale != want)
			{
				t.localScale = want;
			}
		}
	}
}
