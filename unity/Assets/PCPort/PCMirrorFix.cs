using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// PC port: Unity 5.6 draws objects with a negative scale wrongly here: the falling water in the Predator arena became
// huge streaked sheets across the screen, and other props covered the view. The phone game never showed it because
// static batching baked those objects into world space. Each such object gets a copy of its mesh with the scale's
// signs built in (winding reversed when that mirrors it) and a positive scale; its children are moved through the same
// mirror, so everything stays where it was. Runs when a scene loads and again shortly after, for what the level
// spawns at start.
public class PCMirrorFix : MonoBehaviour
{
	private static readonly Dictionary<Mesh, Dictionary<Vector3, Mesh>> mirrored = new Dictionary<Mesh, Dictionary<Vector3, Mesh>>();

	private float again = -1f;

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
		Apply();
		again = Time.realtimeSinceStartup + 3f;
	}

	private void Update()
	{
		if (again > 0f && Time.realtimeSinceStartup >= again)
		{
			again = -1f;
			Apply();
		}
	}

	public static int Apply()
	{
		if (!PCSettings.GetBool("avp_mirror_fix", true))
		{
			return 0;
		}
		// Parents first, so a child whose scale a parent's fix made negative is fixed after it.
		List<Transform> todo = new List<Transform>();
		foreach (Transform t in Object.FindObjectsOfType<Transform>())
		{
			Vector3 s = t.localScale;
			if (s.x < 0f || s.y < 0f || s.z < 0f)
			{
				todo.Add(t);
			}
		}
		todo.Sort((a, b) => Depth(a).CompareTo(Depth(b)));
		int fixedCount = 0;
		int skipped = 0;
		foreach (Transform t in todo)
		{
			if (t == null)
			{
				continue;
			}
			if (Fix(t))
			{
				fixedCount++;
			}
			else
			{
				skipped++;
			}
		}
		if (fixedCount > 0 || skipped > 0)
		{
			Debug.Log("PCMirrorFix: " + fixedCount + " objects with a negative scale redrawn with a positive one, " + skipped + " left as they are");
		}
		return fixedCount;
	}

	private static int Depth(Transform t)
	{
		int d = 0;
		for (Transform p = t.parent; p != null; p = p.parent)
		{
			d++;
		}
		return d;
	}

	// Moves the sign of t's scale into its mesh (and its children's transforms), leaving the world unchanged.
	private static bool Fix(Transform t)
	{
		Vector3 s = t.localScale;
		if (s.x >= 0f && s.y >= 0f && s.z >= 0f)
		{
			return false;
		}
		// Only plain meshes: skinned meshes, particles and animated objects are drawn by other means or rewrite
		// their transforms every frame.
		foreach (Renderer r in t.GetComponents<Renderer>())
		{
			if (!(r is MeshRenderer))
			{
				return false;
			}
		}
		if (Animated(t) || t.GetComponentInChildren<Animation>() != null || t.GetComponentInChildren<Animator>() != null || t.GetComponentInChildren<SkinnedMeshRenderer>() != null)
		{
			return false;
		}
		MeshFilter mf = t.GetComponent<MeshFilter>();
		if (mf != null && mf.sharedMesh != null && !mf.sharedMesh.isReadable)
		{
			return false;
		}
		Vector3 m = new Vector3(Mathf.Sign(s.x), Mathf.Sign(s.y), Mathf.Sign(s.z));
		if (mf != null && mf.sharedMesh != null)
		{
			mf.sharedMesh = Mirror(mf.sharedMesh, m);
		}
		foreach (Collider c in t.GetComponents<Collider>())
		{
			MeshCollider mc = c as MeshCollider;
			BoxCollider bc = c as BoxCollider;
			SphereCollider sc = c as SphereCollider;
			CapsuleCollider cc = c as CapsuleCollider;
			if (mc != null && mc.sharedMesh != null && mc.sharedMesh.isReadable)
			{
				mc.sharedMesh = Mirror(mc.sharedMesh, m);
			}
			else if (bc != null)
			{
				bc.center = Vector3.Scale(bc.center, m);
			}
			else if (sc != null)
			{
				sc.center = Vector3.Scale(sc.center, m);
			}
			else if (cc != null)
			{
				cc.center = Vector3.Scale(cc.center, m);
			}
		}
		// Children keep their place in the world: each is moved, turned and scaled through the same mirror.
		Matrix4x4 mm = Matrix4x4.Scale(m);
		foreach (Transform child in t)
		{
			child.localPosition = Vector3.Scale(child.localPosition, m);
			Matrix4x4 r = mm * Matrix4x4.TRS(Vector3.zero, child.localRotation, Vector3.one) * mm;
			child.localRotation = Quaternion.LookRotation(r.GetColumn(2), r.GetColumn(1));
			child.localScale = Vector3.Scale(child.localScale, m);
		}
		t.localScale = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
		return true;
	}

	private static Mesh Mirror(Mesh src, Vector3 m)
	{
		Dictionary<Vector3, Mesh> bySign;
		if (!mirrored.TryGetValue(src, out bySign))
		{
			bySign = new Dictionary<Vector3, Mesh>();
			mirrored[src] = bySign;
		}
		Mesh copy;
		if (bySign.TryGetValue(m, out copy) && copy != null)
		{
			return copy;
		}
		bool flip = m.x * m.y * m.z < 0f;
		copy = new Mesh();
		copy.name = src.name + " (PC unmirrored)";
		Vector3[] v = src.vertices;
		for (int i = 0; i < v.Length; i++)
		{
			v[i] = Vector3.Scale(v[i], m);
		}
		copy.vertices = v;
		Vector3[] n = src.normals;
		if (n.Length == v.Length)
		{
			for (int i = 0; i < n.Length; i++)
			{
				n[i] = Vector3.Scale(n[i], m);
			}
			copy.normals = n;
		}
		Vector4[] tg = src.tangents;
		if (tg.Length == v.Length)
		{
			for (int i = 0; i < tg.Length; i++)
			{
				tg[i] = new Vector4(tg[i].x * m.x, tg[i].y * m.y, tg[i].z * m.z, flip ? (0f - tg[i].w) : tg[i].w);
			}
			copy.tangents = tg;
		}
		if (src.uv.Length == v.Length)
		{
			copy.uv = src.uv;
		}
		if (src.uv2.Length == v.Length)
		{
			copy.uv2 = src.uv2;
		}
		if (src.uv3.Length == v.Length)
		{
			copy.uv3 = src.uv3;
		}
		if (src.uv4.Length == v.Length)
		{
			copy.uv4 = src.uv4;
		}
		if (src.colors32.Length == v.Length)
		{
			copy.colors32 = src.colors32;
		}
		copy.subMeshCount = src.subMeshCount;
		for (int sub = 0; sub < src.subMeshCount; sub++)
		{
			int[] tri = src.GetTriangles(sub);
			if (flip)
			{
				for (int i = 0; i + 2 < tri.Length; i += 3)
				{
					int k = tri[i + 1];
					tri[i + 1] = tri[i + 2];
					tri[i + 2] = k;
				}
			}
			copy.SetTriangles(tri, sub);
		}
		copy.RecalculateBounds();
		bySign[m] = copy;
		return copy;
	}

	// Animated objects rewrite their transforms every frame, so they are left alone.
	private static bool Animated(Transform t)
	{
		for (Transform p = t; p != null; p = p.parent)
		{
			Animation animation = p.GetComponent<Animation>();
			if (animation != null && animation.GetClipCount() > 0)
			{
				return true;
			}
			Animator animator = p.GetComponent<Animator>();
			if (animator != null && animator.runtimeAnimatorController != null)
			{
				return true;
			}
		}
		return false;
	}
}
