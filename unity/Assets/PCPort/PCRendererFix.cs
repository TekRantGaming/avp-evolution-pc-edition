using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// PC port: in many levels (Alien Missions 3, 4, 6 and 9, Predator Missions 2, 5 and 7...) Unity 5.6 never draws the
// renderers of the un-batched environment meshes (PCPortUnbatch's "<name>_<file id>" meshes): walls and floors are
// missing and the camera looks out at the sky. The mesh, material and lightmap are all fine (a copy of the object
// draws, with any shader); the serialized MeshRenderer itself is what Unity skips, most likely through the per-renderer
// state its lighting data attaches to it. So once a level has started, each of those renderers (on objects without
// scripts of their own) is replaced by a new one with the same materials, lightmap, shadow and sorting settings.
public class PCRendererFix : MonoBehaviour
{
	private static readonly Regex Unbatched = new Regex(@"_\d+( \(PC unmirrored\))?$");

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		// Only the levels: the gameplay, pause and menu scenes' renderers are referenced by the game's scripts.
		bool level = scene.name.StartsWith("Alien_") || scene.name.StartsWith("Predator_") || scene.name.StartsWith("SA") || scene.name.StartsWith("SP") || scene.name == "Bonus_Level" || scene.name == "AVP";
		if (level && PCSettings.GetBool("avp_renderer_fix", true))
		{
			StartCoroutine(FixLater(scene));
		}
	}

	// Only the materials, lightmap, shadows, sorting and enabled state are copied: carrying the light probe, reflection
	// probe, probe anchor, motion vector or vertex stream settings over brought the problem back.
	// What makes Unity skip the renderers is still applied after sceneLoaded, so the renderers are made again once the
	// level has started (its loading screen is gone), the way PCConsole's "rerender" found to work: the old ones are
	// destroyed at the end of a frame and the new ones added in the next.
	private System.Collections.IEnumerator FixLater(Scene scene)
	{
		for (int i = 0; i < 600 && Object.FindObjectOfType<LevelLoader>() != null; i++)
		{
			yield return null;
		}
		yield return null;
		if (!scene.isLoaded)
		{
			yield break;
		}
		List<Saved> saved = new List<Saved>();
		foreach (GameObject root in scene.GetRootGameObjects())
		{
			foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				MeshFilter mf = r.GetComponent<MeshFilter>();
				if (mf != null && mf.sharedMesh != null && Unbatched.IsMatch(mf.sharedMesh.name) && r.GetComponent<MonoBehaviour>() == null)
				{
					if (saved.Count == 0)
					{
						Debug.Log("PCRendererFix: first renderer " + r.name + " probes=" + r.lightProbeUsage + " reflections=" + r.reflectionProbeUsage + " anchor=" + (r.probeAnchor != null ? r.probeAnchor.name : "none") + " motion=" + r.motionVectorGenerationMode + " streams=" + (r.additionalVertexStreams != null ? r.additionalVertexStreams.name : "none"));
					}
					saved.Add(new Saved(r));
					Object.Destroy(r);
				}
			}
		}
		yield return null;
		int count = 0;
		foreach (Saved s in saved)
		{
			if (s.Restore())
			{
				count++;
			}
		}
		if (count > 0)
		{
			Debug.Log("PCRendererFix: " + count + " environment renderers made again in " + scene.name);
		}
		int uvs = FixLightmapOffsets(scene);
		if (uvs > 0)
		{
			Debug.Log("PCRendererFix: " + uvs + " lightmap offsets cleared in " + scene.name);
		}
		originalProbes = null;
		FixProbes();
	}

	// Static batching wrote each object's lightmap offset into its mesh's second UV set, and the un-batched meshes keep
	// those UVs, so the offset the renderer still has was applied twice: every surface read a tiny wrong patch of the
	// lightmap, which gave flat lighting and, where that patch was empty, solid black. A renderer whose UVs already
	// lie inside its own offset rectangle gets the identity offset. This covers every lightmapped renderer of the
	// level, including those Fix leaves alone.
	private static int FixLightmapOffsets(Scene scene)
	{
		const float eps = 0.01f;
		Dictionary<Mesh, Rect> bounds = new Dictionary<Mesh, Rect>();
		int count = 0;
		foreach (GameObject root in scene.GetRootGameObjects())
		{
			foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				Vector4 st = r.lightmapScaleOffset;
				if (r.lightmapIndex < 0 || r.lightmapIndex >= 254 || (st.x == 1f && st.y == 1f && st.z == 0f && st.w == 0f))
				{
					continue;
				}
				MeshFilter mf = r.GetComponent<MeshFilter>();
				if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
				{
					continue;
				}
				Rect uv;
				if (!bounds.TryGetValue(mf.sharedMesh, out uv))
				{
					Vector2[] uv2 = mf.sharedMesh.uv2;
					if (uv2.Length == 0)
					{
						bounds[mf.sharedMesh] = new Rect(-1f, -1f, 0f, 0f);
						continue;
					}
					Vector2 min = uv2[0];
					Vector2 max = uv2[0];
					foreach (Vector2 v in uv2)
					{
						min = Vector2.Min(min, v);
						max = Vector2.Max(max, v);
					}
					uv = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
					bounds[mf.sharedMesh] = uv;
				}
				if (uv.xMin >= st.z - eps && uv.xMax <= st.z + st.x + eps && uv.yMin >= st.w - eps && uv.yMax <= st.w + st.y + eps)
				{
					r.lightmapScaleOffset = new Vector4(1f, 1f, 0f, 0f);
					count++;
				}
			}
		}
		return count;
	}

	private static SphericalHarmonicsL2[] originalProbes;

	// The levels' light probes come out of the export with far too strong directional terms: characters glowed in one
	// colour from one side (the Predator bright orange in Mission 1) and went black from the other. Each probe keeps
	// its average colour (tune_probe_scale) and its directional bands are scaled down (tune_probe_dir).
	public static void FixProbes()
	{
		LightProbes lp = LightmapSettings.lightProbes;
		if (lp == null)
		{
			return;
		}
		if (originalProbes == null || originalProbes.Length != lp.count)
		{
			originalProbes = lp.bakedProbes;
		}
		float scale = PCSettings.GetFloat("tune_probe_scale", 0.85f);
		float dir = PCSettings.GetFloat("tune_probe_dir", 0.3f);
		SphericalHarmonicsL2[] probes = new SphericalHarmonicsL2[originalProbes.Length];
		for (int i = 0; i < probes.Length; i++)
		{
			SphericalHarmonicsL2 sh = originalProbes[i];
			for (int c = 0; c < 3; c++)
			{
				sh[c, 0] *= scale;
				for (int k = 1; k < 4; k++)
				{
					sh[c, k] *= scale * dir;
				}
				for (int k = 4; k < 9; k++)
				{
					sh[c, k] *= scale * dir * dir;
				}
			}
			probes[i] = sh;
		}
		lp.bakedProbes = probes;
	}

	private class Saved
	{
		private readonly GameObject go;
		private readonly Material[] materials;
		private readonly int lightmapIndex;
		private readonly Vector4 lightmapScaleOffset;
		private readonly ShadowCastingMode shadows;
		private readonly bool receiveShadows;
		private readonly int sortingLayer;
		private readonly int sortingOrder;
		private readonly bool enabled;

		public Saved(MeshRenderer r)
		{
			go = r.gameObject;
			materials = r.sharedMaterials;
			lightmapIndex = r.lightmapIndex;
			lightmapScaleOffset = r.lightmapScaleOffset;
			shadows = r.shadowCastingMode;
			receiveShadows = r.receiveShadows;
			sortingLayer = r.sortingLayerID;
			sortingOrder = r.sortingOrder;
			enabled = r.enabled;
		}

		public bool Restore()
		{
			if (go == null || go.GetComponent<MeshRenderer>() != null)
			{
				return false;  // gone, or another component needed the old one so Unity kept it
			}
			MeshRenderer n = go.AddComponent<MeshRenderer>();
			n.sharedMaterials = materials;
			n.lightmapIndex = lightmapIndex;
			n.lightmapScaleOffset = lightmapScaleOffset;
			n.shadowCastingMode = shadows;
			n.receiveShadows = receiveShadows;
			n.sortingLayerID = sortingLayer;
			n.sortingOrder = sortingOrder;
			n.enabled = enabled;
			return true;
		}
	}
}
