using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// PC port: undo the Android build's static batching.
//
// The APK's scenes were static-batched: each MeshRenderer points at a shared "Combined Mesh (root: scene)"
// whose vertices are baked in world space, and picks its piece through the Unity 4 field m_SubsetIndices.
// Unity 5.6 drops that field on upgrade, so every renderer draws submesh 0 of the combined mesh with its own
// transform applied a second time: geometry ends up missing or displaced. This rebuilds a standalone mesh per
// renderer from its original subsets (read from the scene YAML) and moves the vertices back to local space.
//
//   Menu: PC Port > Unbatch Static Meshes (all build scenes)
//   CLI:  -executeMethod PCPortUnbatch.RunAll
public static class PCPortUnbatch
{
	private const string OutRoot = "Assets/Mesh/Unbatched";

	private static readonly Regex DocHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)", RegexOptions.Multiline);

	[MenuItem("PC Port/Unbatch Static Meshes (all build scenes)")]
	public static void RunAll()
	{
		int scenes = 0, fixedRenderers = 0;
		foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
		{
			if (!s.enabled) continue;
			fixedRenderers += RunScene(s.path);
			scenes++;
		}
		AssetDatabase.SaveAssets();
		Debug.Log(string.Format("PCPortUnbatch: done, {0} scenes, {1} renderers rebuilt", scenes, fixedRenderers));
	}

	// MeshRenderer local file id -> subset indices, parsed from the untouched Unity 4 scene YAML.
	private static Dictionary<long, int[]> ReadSubsets(string scenePath)
	{
		var result = new Dictionary<long, int[]>();
		string text = File.ReadAllText(scenePath);
		MatchCollection docs = DocHeader.Matches(text);
		for (int i = 0; i < docs.Count; i++)
		{
			if (docs[i].Groups[1].Value != "23") continue; // 23 = MeshRenderer
			int start = docs[i].Index;
			int end = i + 1 < docs.Count ? docs[i + 1].Index : text.Length;
			Match m = Regex.Match(text.Substring(start, end - start), @"m_SubsetIndices:[ \t]*([0-9a-fA-F]*)");
			if (!m.Success || m.Groups[1].Value.Length == 0) continue;
			string hex = m.Groups[1].Value;
			int[] idx = new int[hex.Length / 8];
			for (int k = 0; k < idx.Length; k++)
			{
				uint le = uint.Parse(hex.Substring(k * 8, 8), NumberStyles.HexNumber);
				idx[k] = (int)(((le & 0xFF) << 24) | ((le & 0xFF00) << 8) | ((le >> 8) & 0xFF00) | (le >> 24));
			}
			result[long.Parse(docs[i].Groups[2].Value)] = idx;
		}
		return result;
	}

	public static int RunScene(string scenePath)
	{
		Dictionary<long, int[]> subsets = ReadSubsets(scenePath);
		if (subsets.Count == 0)
		{
			return 0;
		}
		Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
		string outDir = OutRoot + "/" + Path.GetFileNameWithoutExtension(scenePath);
		if (!AssetDatabase.IsValidFolder(OutRoot)) AssetDatabase.CreateFolder("Assets/Mesh", "Unbatched");
		if (!AssetDatabase.IsValidFolder(outDir)) AssetDatabase.CreateFolder(OutRoot, Path.GetFileNameWithoutExtension(scenePath));

		int rebuilt = 0, skipped = 0;
		// One asset file per scene (meshes as sub-assets): a file per mesh means thousands of imports per scene.
		string bundlePath = outDir + "/" + Path.GetFileNameWithoutExtension(scenePath) + "_Unbatched.asset";
		var created = new List<Mesh>();
		foreach (MeshRenderer r in Resources.FindObjectsOfTypeAll<MeshRenderer>())
		{
			if (r.gameObject.scene != scene) continue;
			MeshFilter mf = r.GetComponent<MeshFilter>();
			if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith("Combined Mesh")) continue;
			long fileId = LocalFileId(r);
			int[] idx;
			if (!subsets.TryGetValue(fileId, out idx) || idx.Length == 0)
			{
				skipped++;
				continue;
			}
			Mesh mesh = Extract(mf.sharedMesh, idx, r.transform.worldToLocalMatrix);
			if (mesh == null)
			{
				skipped++;
				continue;
			}
			mesh.name = r.name + "_" + fileId;
			if (created.Count == 0)
			{
				AssetDatabase.CreateAsset(mesh, bundlePath);
			}
			else
			{
				AssetDatabase.AddObjectToAsset(mesh, bundlePath);
			}
			created.Add(mesh);
			mf.sharedMesh = mesh;
			rebuilt++;
		}
		if (created.Count > 0)
		{
			AssetDatabase.ImportAsset(bundlePath);
		}
		// Saving rewrites the scene in 5.6 format and drops m_SubsetIndices, so only save real progress.
		if (rebuilt > 0)
		{
			EditorSceneManager.MarkSceneDirty(scene);
			EditorSceneManager.SaveScene(scene);
		}
		Debug.Log(string.Format("PCPortUnbatch: {0}: rebuilt {1}, skipped {2}", scenePath, rebuilt, skipped));
		return rebuilt;
	}

	private static Mesh Extract(Mesh src, int[] subMeshes, Matrix4x4 worldToLocal)
	{
		Vector3[] v = src.vertices;
		Vector3[] n = src.normals;
		Vector4[] t = src.tangents;
		Vector2[] uv = src.uv;
		Vector2[] uv2 = src.uv2;
		Color32[] col = src.colors32;
		bool hasN = n.Length == v.Length, hasT = t.Length == v.Length, hasUv = uv.Length == v.Length;
		bool hasUv2 = uv2.Length == v.Length, hasCol = col.Length == v.Length;

		var remap = new Dictionary<int, int>();
		var nv = new List<Vector3>();
		var nn = new List<Vector3>();
		var nt = new List<Vector4>();
		var nuv = new List<Vector2>();
		var nuv2 = new List<Vector2>();
		var ncol = new List<Color32>();
		var tris = new List<int[]>();
		foreach (int sm in subMeshes)
		{
			if (sm < 0 || sm >= src.subMeshCount) return null;
			int[] srcTris = src.GetTriangles(sm);
			int[] dst = new int[srcTris.Length];
			for (int i = 0; i < srcTris.Length; i++)
			{
				int o = srcTris[i];
				int ni;
				if (!remap.TryGetValue(o, out ni))
				{
					ni = nv.Count;
					remap[o] = ni;
					nv.Add(worldToLocal.MultiplyPoint3x4(v[o]));
					if (hasN) nn.Add(worldToLocal.MultiplyVector(n[o]).normalized);
					if (hasT)
					{
						Vector3 td = worldToLocal.MultiplyVector(new Vector3(t[o].x, t[o].y, t[o].z)).normalized;
						nt.Add(new Vector4(td.x, td.y, td.z, t[o].w));
					}
					if (hasUv) nuv.Add(uv[o]);
					if (hasUv2) nuv2.Add(uv2[o]);
					if (hasCol) ncol.Add(col[o]);
				}
				dst[i] = ni;
			}
			tris.Add(dst);
		}

		Mesh m = new Mesh();
		m.SetVertices(nv);
		if (hasN) m.SetNormals(nn);
		if (hasT) m.SetTangents(nt);
		if (hasUv) m.SetUVs(0, nuv);
		if (hasUv2) m.SetUVs(1, nuv2);
		if (hasCol) m.SetColors(ncol);
		// Static batching baked mirrored (negative-scale) objects into world space with their winding already
		// flipped; moving the vertices back into local space re-applies the mirror, so flip back.
		if (worldToLocal.determinant < 0f)
		{
			foreach (int[] tri in tris) ReverseWinding(tri);
		}
		m.subMeshCount = tris.Count;
		for (int i = 0; i < tris.Count; i++) m.SetTriangles(tris[i], i);
		m.RecalculateBounds();
		return m;
	}

	private static void ReverseWinding(int[] tri)
	{
		for (int i = 0; i + 2 < tri.Length; i += 3)
		{
			int tmp = tri[i + 1];
			tri[i + 1] = tri[i + 2];
			tri[i + 2] = tmp;
		}
	}

	private const string MirrorFixedList = "ProjectSettings/PCPortMirrorFixed.txt";

	// One-time repair for scenes un-batched before the extractor flipped mirrored meshes. Scenes already
	// repaired are recorded in MirrorFixedList so the winding is never flipped twice.
	//   CLI: -executeMethod PCPortUnbatch.FixMirroredAll
	public static void FixMirroredAll()
	{
		var done = new HashSet<string>(File.Exists(MirrorFixedList) ? File.ReadAllLines(MirrorFixedList) : new string[0]);
		foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
		{
			if (!s.enabled || done.Contains(s.path)) continue;
			Scene scene = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
			int flipped = 0;
			foreach (MeshFilter mf in Resources.FindObjectsOfTypeAll<MeshFilter>())
			{
				if (mf.gameObject.scene != scene || mf.sharedMesh == null) continue;
				if (!AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(OutRoot)) continue;
				if (mf.transform.localToWorldMatrix.determinant >= 0f) continue;
				Mesh m = mf.sharedMesh;
				for (int sm = 0; sm < m.subMeshCount; sm++)
				{
					int[] tri = m.GetTriangles(sm);
					ReverseWinding(tri);
					m.SetTriangles(tri, sm);
				}
				EditorUtility.SetDirty(m);
				flipped++;
			}
			AssetDatabase.SaveAssets();
			done.Add(s.path);
			File.WriteAllLines(MirrorFixedList, new List<string>(done).ToArray());
			Debug.Log("PCPortUnbatch: " + s.path + ": flipped " + flipped + " mirrored meshes");
		}
	}

	private static readonly PropertyInfo InspectorModeProp =
		typeof(SerializedObject).GetProperty("inspectorMode", BindingFlags.NonPublic | BindingFlags.Instance);

	// Unsupported.GetLocalIdentifierInFile returns 0 for scene objects in 5.6; the Debug inspector
	// exposes the scene file id as m_LocalIdentfierInFile (sic).
	internal static long LocalFileId(UnityEngine.Object o)
	{
		SerializedObject so = new SerializedObject(o);
		InspectorModeProp.SetValue(so, InspectorMode.Debug, null);
		SerializedProperty p = so.FindProperty("m_LocalIdentfierInFile");
		return p != null ? p.longValue : 0;
	}

	private static string Sanitize(string s)
	{
		foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
		return s;
	}
}
