using UnityEngine;

// PC port: engine behaviour Unity 4 had by default that Unity 5 changed.
public static class PCPortBootstrap
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		// Unity 4 (PhysX 2) raycasts hit mesh-collider back faces; Unity 5 (PhysX 3) skips them. MainCamera keeps the
		// third-person camera inside corridors with Linecasts against the environment collision meshes, which are
		// modelled facing outward, so without this the camera rises through ceilings and the level looks missing.
		Physics.queriesHitBackfaces = true;
	}
}
