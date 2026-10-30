using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// PC port: diagnostics. Unity.exe -batchmode -quit -projectPath <proj> -executeMethod PCPortProbe.Run -logFile probe.log
public static class PCPortProbe
{
	public static void Run()
	{
		EditorSceneManager.OpenScene("Assets/Scenes/AVP/Alien_L1.unity");
		MeshRenderer[] renderers = Object.FindObjectsOfType<MeshRenderer>();
		Debug.Log("PROBE renderers: " + renderers.Length);
		int shown = 0;
		foreach (MeshRenderer r in renderers)
		{
			MeshFilter mf = r.GetComponent<MeshFilter>();
			Mesh m = mf ? mf.sharedMesh : null;
			SerializedObject so = new SerializedObject(r);
			SerializedProperty first = so.FindProperty("m_StaticBatchInfo.firstSubMesh");
			SerializedProperty count = so.FindProperty("m_StaticBatchInfo.subMeshCount");
			SerializedProperty root = so.FindProperty("m_StaticBatchRoot");
			if (shown++ < 12)
			{
				Debug.Log(string.Format("PROBE {0} mesh='{1}' sub={2} verts={3} first={4} count={5} root={6} mats={7} shader={8}",
					r.name, m ? m.name : "null", m ? m.subMeshCount : -1, m ? m.vertexCount : -1,
					first != null ? first.intValue.ToString() : "n/a", count != null ? count.intValue.ToString() : "n/a",
					root != null && root.objectReferenceValue ? root.objectReferenceValue.name : "none",
					r.sharedMaterials.Length, r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "null"));
			}
		}
		var byMesh = renderers.Select(r => r.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh)
			.GroupBy(f => f.sharedMesh.name).OrderByDescending(g => g.Count()).Take(8);
		foreach (var g in byMesh) Debug.Log("PROBE meshuse " + g.Key + " x" + g.Count());
		SkinnedMeshRenderer[] skinned = Object.FindObjectsOfType<SkinnedMeshRenderer>();
		Debug.Log("PROBE skinned: " + skinned.Length);
		foreach (SkinnedMeshRenderer s in skinned.Take(10))
		{
			Mesh m = s.sharedMesh;
			Debug.Log(string.Format("PROBE skin {0} mesh='{1}' verts={2} bones={3} bind={4} weights={5} shader={6}",
				s.name, m ? m.name : "null", m ? m.vertexCount : -1, s.bones.Length, m ? m.bindposes.Length : -1,
				m ? m.boneWeights.Length : -1, s.sharedMaterial && s.sharedMaterial.shader ? s.sharedMaterial.shader.name : "null"));
		}
		foreach (Animation a in Object.FindObjectsOfType<Animation>().Take(15))
		{
			string clips = string.Join(",", AnimationUtility.GetAnimationClips(a.gameObject).Where(c => c).Select(c => c.name + (c.legacy ? "" : "(!legacy)")).Take(6).ToArray());
			Debug.Log(string.Format("PROBE anim {0} count={1} default={2} clips=[{3}]", a.name, a.GetClipCount(), a.clip ? a.clip.name + (a.clip.legacy ? "" : "(!legacy)") : "null", clips));
		}
		string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
		int legacy = 0, nonLegacy = 0;
		foreach (string g in guids)
		{
			AnimationClip c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g));
			if (c == null) continue;
			if (c.legacy) legacy++; else nonLegacy++;
		}
		Debug.Log("PROBE clips legacy=" + legacy + " nonLegacy=" + nonLegacy);
		Shader[] broken = Resources.FindObjectsOfTypeAll<Shader>().Where(s => !s.isSupported).ToArray();
		foreach (Shader s in broken) Debug.Log("PROBE unsupported shader: " + s.name);
	}
	// -executeMethod PCPortProbe.At  (scene + point hard-coded for the current investigation)
	public static void At()
	{
		EditorSceneManager.OpenScene("Assets/Scenes/AVP/Predator_L4.unity");
		Vector3 p = new Vector3(40.7f, 1f, 13.8f);
		foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
		{
			if (!r.gameObject.scene.IsValid()) continue;
			Bounds b = r.bounds;
			b.Expand(4f);
			if (!b.Contains(p)) continue;
			MeshFilter mf = r.GetComponent<MeshFilter>();
			Mesh m = mf ? mf.sharedMesh : null;
			Debug.Log(string.Format("PROBEAT {0} active={1} enabled={2} mesh={3} verts={4} bounds={5} shader={6} path={7}",
				r.name, r.gameObject.activeInHierarchy, r.enabled, m ? m.name : "null", m ? m.vertexCount : -1, r.bounds,
				r.sharedMaterial && r.sharedMaterial.shader ? r.sharedMaterial.shader.name : "null", AnimationUtility.CalculateTransformPath(r.transform, null)));
		}
	}
}
