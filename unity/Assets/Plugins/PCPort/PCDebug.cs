using UnityEngine;

// PC port: press F9 in game to log what is at the centre of the screen (and straight below it) to
// output_log.txt: object, shader, lightmap slot and whether the mesh has lightmap UVs. Used to track down
// surfaces that render wrong after the Unity 4 -> 5 upgrade.
public class PCDebug : MonoBehaviour
{
	[RuntimeInitializeOnLoadMethod]
	private static void Install()
	{
		GameObject go = new GameObject("PCDebug");
		go.hideFlags = HideFlags.HideAndDontSave;
		Object.DontDestroyOnLoad(go);
		go.AddComponent<PCDebug>();
	}

	private void Update()
	{
		if (!Input.GetKeyDown(KeyCode.F9))
		{
			return;
		}
		Camera cam = Camera.main;
		if (cam == null)
		{
			Debug.Log("PCDebug: no main camera");
			return;
		}
		Ray centre = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
		Ray down = cam.ViewportPointToRay(new Vector3(0.5f, 0.15f, 0f));
		Debug.Log("PCDebug: camera at " + cam.transform.position + " lightmaps=" + LightmapSettings.lightmaps.Length);
		foreach (RaycastHit hit in Physics.RaycastAll(centre, 500f))
		{
			Report("centre", hit);
		}
		foreach (RaycastHit hit in Physics.RaycastAll(down, 500f))
		{
			Report("lower", hit);
		}
		ReportView(cam);
	}

	// Lightmap state plus the largest renderers in view: what the camera actually draws.
	private static void ReportView(Camera cam)
	{
		LightmapData[] maps = LightmapSettings.lightmaps;
		for (int i = 0; i < maps.Length; i++)
		{
			Texture2D t = maps[i].lightmapColor;
			Debug.Log(string.Format("PCDebug lightmap[{0}] = {1} {2}x{3} {4}", i, t ? t.name : "null", t ? t.width : 0, t ? t.height : 0, t ? t.format.ToString() : ""));
		}
		Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
		var seen = new System.Collections.Generic.List<Renderer>();
		int lit = 0, total = 0;
		foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
		{
			if (!r.enabled || !r.isVisible || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
			total++;
			if (r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE) lit++;
			seen.Add(r);
		}
		seen.Sort((a, b) => b.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));
		Debug.Log(string.Format("PCDebug view: {0} visible renderers, {1} lightmapped, ambient={2} mode={3}", total, lit, RenderSettings.ambientLight, RenderSettings.ambientMode));
		for (int i = 0; i < seen.Count && i < 12; i++)
		{
			Renderer r = seen[i];
			MeshFilter mf = r.GetComponent<MeshFilter>();
			Mesh m = mf ? mf.sharedMesh : null;
			Material mat = r.sharedMaterial;
			Debug.Log(string.Format("PCDebug view[{0}] {1} shader={2} tex={3} lightmap={4} offset={5} mesh={6} uv2={7}", i, r.name,
				mat && mat.shader ? mat.shader.name : "null", mat && mat.mainTexture ? mat.mainTexture.name : "null",
				r.lightmapIndex, r.lightmapScaleOffset, m ? m.name : "null", m ? m.uv2.Length : -1));
		}
	}

	private static void Report(string label, RaycastHit hit)
	{
		Renderer r = hit.collider.GetComponent<Renderer>();
		string info = "no renderer";
		if (r != null)
		{
			MeshFilter mf = r.GetComponent<MeshFilter>();
			Mesh m = mf != null ? mf.sharedMesh : null;
			info = string.Format("renderer enabled={0} shader={1} lightmap={2} offset={3} mesh={4} verts={5} uv2={6}",
				r.enabled, r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "null",
				r.lightmapIndex, r.lightmapScaleOffset, m != null ? m.name : "null", m != null ? m.vertexCount : -1,
				m != null ? m.uv2.Length : -1);
		}
		Debug.Log(string.Format("PCDebug {0}: {1} (layer {2}) dist {3:F1} {4}", label, hit.collider.name, LayerMask.LayerToName(hit.collider.gameObject.layer), hit.distance, info));
	}
}
