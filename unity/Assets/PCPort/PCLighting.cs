using UnityEngine;
using UnityEngine.SceneManagement;

// PC port: the launcher's "Enhanced lighting". The levels' torches, fires and lamps are baked into the lightmaps
// only, so the Predator, the Aliens and the Marines never pick up their colour. This gives every baked point and spot
// light a realtime twin that lights only the characters' layers (the lightmapped world keeps its baked light), and
// lets Unity light them per pixel (the characters' mobile shaders no longer skip extra lights: noforwardadd was taken
// out of Mobile/Bumped Specular, Mobile/Bumped Diffuse, Mobile/Diffuse and AMG/Bumped Specular RimLight).
// A level has hundreds of these lights, strong and only a few metres wide, so their strength is capped and the
// smallest are left out: otherwise each small lamp flares on whoever passes it, and they pop as the 8 per-pixel slots
// change hands.
public class PCLighting : MonoBehaviour
{
	private const string TwinName = "PCPort realtime light";

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
	}

	public static void Apply()
	{
		bool on = PCSettings.GetBool("avp_enhanced_lighting", true);
		int mask = LayerMask.GetMask("Player", "Enemies");
		float scale = PCSettings.GetFloat("tune_char_lights", 0.25f);
		float cap = PCSettings.GetFloat("tune_char_light_max", 0.8f);
		float smallest = PCSettings.GetFloat("tune_char_light_min_range", 2.5f);
		int made = 0;
		foreach (Light light in Object.FindObjectsOfType<Light>())
		{
			if (light.name == TwinName)
			{
				light.gameObject.SetActive(on);
				light.intensity = Mathf.Min(light.transform.parent.GetComponent<Light>().intensity * scale, cap);
				continue;
			}
			if (!light.isBaked || (light.type != LightType.Point && light.type != LightType.Spot) || light.range < smallest || light.transform.Find(TwinName) != null)
			{
				continue;
			}
			if (!on)
			{
				continue;
			}
			GameObject go = new GameObject(TwinName);
			go.transform.SetParent(light.transform, false);
			Light twin = go.AddComponent<Light>();
			twin.type = light.type;
			twin.color = light.color;
			twin.intensity = Mathf.Min(light.intensity * scale, cap);
			twin.range = light.range;
			twin.spotAngle = light.spotAngle;
			twin.shadows = LightShadows.None;
			twin.cullingMask = mask;
			twin.renderMode = LightRenderMode.Auto;
			go.SetActive(light.isActiveAndEnabled);
			made++;
		}
		if (made > 0)
		{
			Debug.Log("PCLighting: " + made + " realtime lights for the characters");
		}
	}
}
