using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds a "dressed" map scene: drops in the course prefab, surrounds it with an outer ground sized to the
// course bounds, and applies a skybox and distance fog so the hard horizon edge is hidden.
public class MapEnvironmentSetup : EditorWindow
{
	private const string MenuPath = "Tools/Driving Test/Map Environment Setup";
	private const string MapPrefabPath = "Assets/Resources/MapPrefab/옥천학원수정.prefab";
	private const string OuterGroundName = "Generated Outer Ground";

	// Unity's built-in Plane mesh is 10x10 meters at unit scale, so world size divides by this to get the scale.
	private const float PlaneMeshSize = 10f;

	// Sunk slightly below the course base plane to avoid z-fighting where the two overlap.
	private const float OuterGroundDepthOffset = 0.05f;

	// Falls back to this footprint (meters) only when no renderers are found to measure.
	private const float FallbackMapSize = 100f;

	// Trees are parented under one root so re-runs can clear and replant them as a group.
	private const string PerimeterTreesName = "Generated Perimeter Trees";

	// Guards against a tiny spacing or huge margin spawning an unmanageable number of trees.
	private const int MaxTreeCount = 5000;

	[SerializeField] private GameObject mapPrefab;
	[SerializeField] private Material skyboxMaterial;
	[SerializeField] private Material outerGroundMaterial;
	[SerializeField] private float outerGroundMargin = 300f;

	[SerializeField] private bool applyFog = true;
	[SerializeField] private FogMode fogMode = FogMode.Linear;
	[SerializeField] private Color fogColor = new Color(0.62f, 0.69f, 0.78f, 1f);
	[SerializeField] private float fogStartDistance = 150f;
	[SerializeField] private float fogEndDistance = 400f;

	[SerializeField] private List<GameObject> treePrefabs = new List<GameObject>();
	[SerializeField] private int treeRingCount = 3;
	[SerializeField] private float treeFirstRingOffset = 12f;
	[SerializeField] private float treeRingSpacing = 8f;
	[SerializeField] private float treeSpacingAlongRing = 10f;
	[SerializeField] private float treePositionJitter = 2f;
	[SerializeField] private Vector2 treeScaleRange = new Vector2(0.8f, 1.2f);
	[SerializeField] private int treeRandomSeed = 12345;

	[SerializeField] private string newScenePath = "Assets/Scenes/Map_Dressed.unity";

	private Vector2 scrollPosition;

	[MenuItem(MenuPath)]
	private static void Open()
	{
		MapEnvironmentSetup window = GetWindow<MapEnvironmentSetup>(true, "Map Environment Setup", true);
		window.minSize = new Vector2(320f, 480f);
	}

	private void OnEnable()
	{
		if (mapPrefab == null)
		{
			mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
		}
	}

	private void OnGUI()
	{
		scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

		EditorGUILayout.LabelField("Map", EditorStyles.boldLabel);
		mapPrefab = (GameObject)EditorGUILayout.ObjectField("Map Prefab", mapPrefab, typeof(GameObject), false);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Environment", EditorStyles.boldLabel);
		skyboxMaterial = (Material)EditorGUILayout.ObjectField("Skybox Material", skyboxMaterial, typeof(Material), false);
		outerGroundMaterial = (Material)EditorGUILayout.ObjectField("Outer Ground Material", outerGroundMaterial, typeof(Material), false);
		outerGroundMargin = Mathf.Max(0f, EditorGUILayout.FloatField("Outer Ground Margin (m)", outerGroundMargin));

		EditorGUILayout.Space();
		applyFog = EditorGUILayout.BeginToggleGroup("Apply Fog", applyFog);
		fogMode = (FogMode)EditorGUILayout.EnumPopup("Fog Mode", fogMode);
		fogColor = EditorGUILayout.ColorField("Fog Color", fogColor);
		fogStartDistance = EditorGUILayout.FloatField("Fog Start (m)", fogStartDistance);
		fogEndDistance = EditorGUILayout.FloatField("Fog End (m)", fogEndDistance);
		EditorGUILayout.EndToggleGroup();

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Perimeter Trees", EditorStyles.boldLabel);
		int prefabCount = Mathf.Max(0, EditorGUILayout.IntField("Tree Prefab Count", treePrefabs.Count));
		while (treePrefabs.Count < prefabCount)
		{
			treePrefabs.Add(null);
		}
		while (treePrefabs.Count > prefabCount)
		{
			treePrefabs.RemoveAt(treePrefabs.Count - 1);
		}
		for (int i = 0; i < treePrefabs.Count; i++)
		{
			treePrefabs[i] = (GameObject)EditorGUILayout.ObjectField($"Tree {i}", treePrefabs[i], typeof(GameObject), false);
		}
		treeRingCount = Mathf.Max(0, EditorGUILayout.IntField("Ring Count", treeRingCount));
		treeFirstRingOffset = EditorGUILayout.FloatField("First Ring Offset (m)", treeFirstRingOffset);
		treeRingSpacing = EditorGUILayout.FloatField("Ring Spacing (m)", treeRingSpacing);
		treeSpacingAlongRing = EditorGUILayout.FloatField("Spacing Along Ring (m)", treeSpacingAlongRing);
		treePositionJitter = Mathf.Max(0f, EditorGUILayout.FloatField("Position Jitter (m)", treePositionJitter));
		treeScaleRange = EditorGUILayout.Vector2Field("Scale Range (min/max)", treeScaleRange);
		treeRandomSeed = EditorGUILayout.IntField("Random Seed", treeRandomSeed);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("New Scene", EditorStyles.boldLabel);
		newScenePath = EditorGUILayout.TextField("Scene Path", newScenePath);

		EditorGUILayout.Space();
		if (GUILayout.Button("Create New Scene + Apply", GUILayout.Height(30f)))
		{
			CreateDressedScene();
		}

		if (GUILayout.Button("Apply To Current Scene", GUILayout.Height(24f)))
		{
			ApplyToActiveScene();
		}

		if (GUILayout.Button("Plant Perimeter Trees (Current Scene)", GUILayout.Height(24f)))
		{
			PlantTreesInActiveScene();
		}

		if (GUILayout.Button("Fix Map Materials (URP)", GUILayout.Height(24f)))
		{
			FixMapMaterialsInActiveScene();
		}

		EditorGUILayout.Space();
		EditorGUILayout.HelpBox(
			"Skybox/ground materials come from your imported Asset Store packages (AllSky, URP Tree Models). "
			+ "Fog uses the built-in render settings, which URP honors.",
			MessageType.Info);

		EditorGUILayout.EndScrollView();
	}

	// Creates a fresh scene, instantiates the course, then surrounds and dresses it so it can be previewed on its own.
	private void CreateDressedScene()
	{
		if (!ValidateMapPrefab() || !ValidateNewScenePath())
		{
			return;
		}

		if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
		{
			return;
		}

		Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

		GameObject mapInstance = (GameObject)PrefabUtility.InstantiatePrefab(mapPrefab, scene);
		mapInstance.transform.position = Vector3.zero;

		if (!TryComputeContentBounds(scene, out Bounds mapBounds))
		{
			mapBounds = new Bounds(Vector3.zero, new Vector3(FallbackMapSize, 0f, FallbackMapSize));
			Debug.LogWarning($"MapEnvironmentSetup: No renderers found to measure; using a {FallbackMapSize}m fallback footprint.");
		}

		BuildEnvironment(scene, mapBounds);

		Directory.CreateDirectory(Path.GetDirectoryName(newScenePath));
		bool saved = EditorSceneManager.SaveScene(scene, newScenePath);
		if (!saved)
		{
			Debug.LogError($"MapEnvironmentSetup: Failed to save the new scene at '{newScenePath}'.");
			return;
		}

		AssetDatabase.Refresh();
		Debug.Log($"MapEnvironmentSetup: Created '{newScenePath}' around a {mapBounds.size.x:F0}x{mapBounds.size.z:F0}m course.");
	}

	// Applies the outer ground, skybox, and fog to whatever scene is open, measuring the course already in it.
	private void ApplyToActiveScene()
	{
		Scene scene = SceneManager.GetActiveScene();
		if (!TryComputeContentBounds(scene, out Bounds mapBounds))
		{
			Debug.LogError($"MapEnvironmentSetup: No renderers found in '{scene.name}' to measure the course.");
			return;
		}

		BuildEnvironment(scene, mapBounds);
		EditorSceneManager.MarkSceneDirty(scene);
		Debug.Log($"MapEnvironmentSetup: Dressed '{scene.name}' around a {mapBounds.size.x:F0}x{mapBounds.size.z:F0}m course. Save the scene to keep it.");
	}

	// Replants the perimeter trees on the open scene without touching the ground, skybox, or fog.
	private void PlantTreesInActiveScene()
	{
		Scene scene = SceneManager.GetActiveScene();
		if (!TryComputeContentBounds(scene, out Bounds mapBounds))
		{
			Debug.LogError($"MapEnvironmentSetup: No renderers found in '{scene.name}' to measure the course.");
			return;
		}

		PlantPerimeterTrees(scene, mapBounds);
		EditorSceneManager.MarkSceneDirty(scene);
	}

	// Swaps non-URP (magenta) materials on the open scene's renderers for URP ones, as scene-instance overrides only.
	private void FixMapMaterialsInActiveScene()
	{
		Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
		if (urpLitShader == null)
		{
			Debug.LogError("MapEnvironmentSetup: 'Universal Render Pipeline/Lit' shader not found; is URP installed?");
			return;
		}

		Scene scene = SceneManager.GetActiveScene();
		var replacementsBySource = new Dictionary<Material, Material>();
		int replacedSlotCount = 0;

		foreach (GameObject rootObject in scene.GetRootGameObjects())
		{
			if (rootObject.name == OuterGroundName || rootObject.name == PerimeterTreesName)
			{
				continue;
			}

			foreach (Renderer renderer in rootObject.GetComponentsInChildren<Renderer>(true))
			{
				Material[] sharedMaterials = renderer.sharedMaterials;
				bool rendererChanged = false;

				for (int slot = 0; slot < sharedMaterials.Length; slot++)
				{
					if (!NeedsUrpReplacement(sharedMaterials[slot]))
					{
						continue;
					}

					sharedMaterials[slot] = GetOrCreateReplacement(sharedMaterials[slot], urpLitShader, replacementsBySource);
					rendererChanged = true;
					replacedSlotCount++;
				}

				if (rendererChanged)
				{
					Undo.RecordObject(renderer, "Fix Map Materials");
					renderer.sharedMaterials = sharedMaterials;
				}
			}
		}

		EditorSceneManager.MarkSceneDirty(scene);
		Debug.Log($"MapEnvironmentSetup: Replaced {replacedSlotCount} non-URP material slot(s) with URP materials on '{scene.name}'. Save the scene to keep it.");
	}

	private void BuildEnvironment(Scene scene, Bounds mapBounds)
	{
		CreateOuterGround(scene, mapBounds);
		ApplySkybox();
		ApplyFogSettings();
		PlantPerimeterTrees(scene, mapBounds);
	}

	// Lays a plane just past the course on every side so its far edge sits beyond the fog, hiding the ground seam.
	private void CreateOuterGround(Scene scene, Bounds mapBounds)
	{
		GameObject existingGround = FindRootByName(scene, OuterGroundName);
		if (existingGround != null)
		{
			Undo.DestroyObjectImmediate(existingGround);
		}

		GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
		ground.name = OuterGroundName;
		Undo.RegisterCreatedObjectUndo(ground, "Create Outer Ground");

		float groundWidth = mapBounds.size.x + (outerGroundMargin * 2f);
		float groundDepth = mapBounds.size.z + (outerGroundMargin * 2f);
		ground.transform.localScale = new Vector3(groundWidth / PlaneMeshSize, 1f, groundDepth / PlaneMeshSize);
		ground.transform.position = new Vector3(mapBounds.center.x, mapBounds.min.y - OuterGroundDepthOffset, mapBounds.center.z);

		MeshRenderer groundRenderer = ground.GetComponent<MeshRenderer>();
		if (outerGroundMaterial != null)
		{
			groundRenderer.sharedMaterial = outerGroundMaterial;
		}
		else
		{
			// Primitives spawn with the Built-in default material, which renders magenta under URP; swap in a URP-lit material.
			Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
			if (urpLitShader != null)
			{
				groundRenderer.sharedMaterial = new Material(urpLitShader) { color = new Color(0.45f, 0.5f, 0.42f) };
			}
		}

		GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
		ground.transform.SetAsLastSibling();
	}

	private void ApplySkybox()
	{
		if (skyboxMaterial == null)
		{
			return;
		}

		RenderSettings.skybox = skyboxMaterial;
		DynamicGI.UpdateEnvironment();
	}

	private void ApplyFogSettings()
	{
		RenderSettings.fog = applyFog;
		if (!applyFog)
		{
			return;
		}

		RenderSettings.fogMode = fogMode;
		RenderSettings.fogColor = fogColor;
		if (fogMode == FogMode.Linear)
		{
			RenderSettings.fogStartDistance = fogStartDistance;
			RenderSettings.fogEndDistance = fogEndDistance;
		}
	}

	// Surrounds the course with concentric rectangular rings of trees so the ground seam is walled off from view.
	private void PlantPerimeterTrees(Scene scene, Bounds mapBounds)
	{
		List<GameObject> validPrefabs = CollectValidTreePrefabs();
		if (validPrefabs.Count == 0 || treeRingCount <= 0)
		{
			return;
		}

		GameObject existingRoot = FindRootByName(scene, PerimeterTreesName);
		if (existingRoot != null)
		{
			Undo.DestroyObjectImmediate(existingRoot);
		}

		var treesRoot = new GameObject(PerimeterTreesName);
		SceneManager.MoveGameObjectToScene(treesRoot, scene);
		Undo.RegisterCreatedObjectUndo(treesRoot, "Plant Perimeter Trees");

		// Seed and restore so the layout is reproducible without disturbing the rest of the editor session.
		Random.State previousRandomState = Random.state;
		Random.InitState(treeRandomSeed);

		float groundY = mapBounds.min.y;
		int plantedCount = 0;
		for (int ring = 0; ring < treeRingCount && plantedCount < MaxTreeCount; ring++)
		{
			float ringInset = treeFirstRingOffset + (ring * treeRingSpacing);
			float ringHalfX = mapBounds.extents.x + ringInset;
			float ringHalfZ = mapBounds.extents.z + ringInset;
			plantedCount += PlaceTreeRing(treesRoot.transform, validPrefabs, mapBounds.center, ringHalfX, ringHalfZ, groundY, MaxTreeCount - plantedCount);
		}

		Random.state = previousRandomState;

		if (plantedCount >= MaxTreeCount)
		{
			Debug.LogWarning($"MapEnvironmentSetup: Reached the {MaxTreeCount} tree safety cap. Increase spacing or reduce ring count/margin.");
		}

		Debug.Log($"MapEnvironmentSetup: Planted {plantedCount} trees in up to {treeRingCount} ring(s).");
	}

	// Walks the four edges of one rectangular ring, dropping a tree every spacing step with a little jitter.
	private int PlaceTreeRing(Transform parent, List<GameObject> prefabs, Vector3 center, float halfX, float halfZ, float groundY, int remaining)
	{
		if (remaining <= 0)
		{
			return 0;
		}

		Vector2[] corners =
		{
			new Vector2(-halfX, -halfZ),
			new Vector2(halfX, -halfZ),
			new Vector2(halfX, halfZ),
			new Vector2(-halfX, halfZ),
		};

		float spacing = Mathf.Max(0.1f, treeSpacingAlongRing);
		int placedCount = 0;
		for (int edge = 0; edge < corners.Length; edge++)
		{
			Vector2 edgeStart = corners[edge];
			Vector2 edgeEnd = corners[(edge + 1) % corners.Length];
			int stepCount = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(edgeStart, edgeEnd) / spacing));

			// Each edge includes its start point but not its end, so the shared corner is not planted twice.
			for (int step = 0; step < stepCount; step++)
			{
				if (placedCount >= remaining)
				{
					return placedCount;
				}

				Vector2 edgePoint = Vector2.Lerp(edgeStart, edgeEnd, (float)step / stepCount);
				float jitterX = Random.Range(-treePositionJitter, treePositionJitter);
				float jitterZ = Random.Range(-treePositionJitter, treePositionJitter);
				Vector3 treePosition = new Vector3(center.x + edgePoint.x + jitterX, groundY, center.z + edgePoint.y + jitterZ);

				PlantTree(parent, prefabs, treePosition);
				placedCount++;
			}
		}

		return placedCount;
	}

	// Instantiates a random prefab as a linked prefab instance with a random yaw and uniform scale within range.
	private void PlantTree(Transform parent, List<GameObject> prefabs, Vector3 treePosition)
	{
		GameObject prefab = prefabs[Random.Range(0, prefabs.Count)];
		var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
		tree.transform.SetParent(parent, true);
		tree.transform.SetPositionAndRotation(treePosition, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

		float scaleFactor = Random.Range(treeScaleRange.x, treeScaleRange.y);
		tree.transform.localScale = prefab.transform.localScale * scaleFactor;
	}

	private List<GameObject> CollectValidTreePrefabs()
	{
		var validPrefabs = new List<GameObject>();
		foreach (GameObject prefab in treePrefabs)
		{
			if (prefab != null)
			{
				validPrefabs.Add(prefab);
			}
		}

		return validPrefabs;
	}

	// True when a slot is empty or uses a Built-in/legacy shader that renders magenta under URP.
	private static bool NeedsUrpReplacement(Material material)
	{
		if (material == null || material.shader == null)
		{
			return true;
		}

		string shaderName = material.shader.name;
		if (shaderName.StartsWith("Universal Render Pipeline/") || shaderName.StartsWith("URP/"))
		{
			return false;
		}

		return shaderName == "Standard"
			|| shaderName == "Standard (Specular setup)"
			|| shaderName == "Diffuse"
			|| shaderName.StartsWith("Legacy Shaders/")
			|| shaderName.StartsWith("Mobile/");
	}

	// Reuses one URP material per source so identical source materials don't spawn duplicates.
	private static Material GetOrCreateReplacement(Material source, Shader urpLitShader, Dictionary<Material, Material> replacementsBySource)
	{
		// A null slot has no source to key on, so it always gets a fresh generic material.
		if (source == null)
		{
			return CreateUrpReplacement(null, urpLitShader);
		}

		if (replacementsBySource.TryGetValue(source, out Material existing))
		{
			return existing;
		}

		Material replacement = CreateUrpReplacement(source, urpLitShader);
		replacementsBySource[source] = replacement;
		return replacement;
	}

	// Carries the source color and main texture across so lines/surfaces keep their look under URP.
	private static Material CreateUrpReplacement(Material source, Shader urpLitShader)
	{
		var replacement = new Material(urpLitShader);
		if (source == null)
		{
			replacement.name = "Map Material (URP)";
			return replacement;
		}

		replacement.name = $"{source.name} (URP)";
		if (source.shader == null)
		{
			return replacement;
		}

		if (source.HasProperty("_Color"))
		{
			replacement.color = source.GetColor("_Color");
		}
		else if (source.HasProperty("_BaseColor"))
		{
			replacement.color = source.GetColor("_BaseColor");
		}

		if (source.mainTexture != null)
		{
			replacement.mainTexture = source.mainTexture;
		}

		return replacement;
	}

	// Encapsulates every renderer except the generated ground, so the measurement reflects the course alone.
	private static bool TryComputeContentBounds(Scene scene, out Bounds bounds)
	{
		bounds = default;
		bool hasBounds = false;

		foreach (GameObject rootObject in scene.GetRootGameObjects())
		{
			if (rootObject.name == OuterGroundName || rootObject.name == PerimeterTreesName)
			{
				continue;
			}

			foreach (Renderer renderer in rootObject.GetComponentsInChildren<Renderer>(true))
			{
				if (!hasBounds)
				{
					bounds = renderer.bounds;
					hasBounds = true;
				}
				else
				{
					bounds.Encapsulate(renderer.bounds);
				}
			}
		}

		return hasBounds;
	}

	private static GameObject FindRootByName(Scene scene, string rootName)
	{
		foreach (GameObject rootObject in scene.GetRootGameObjects())
		{
			if (rootObject.name == rootName)
			{
				return rootObject;
			}
		}

		return null;
	}

	private bool ValidateMapPrefab()
	{
		if (mapPrefab != null)
		{
			return true;
		}

		Debug.LogError($"MapEnvironmentSetup: Assign a map prefab (default '{MapPrefabPath}').");
		return false;
	}

	private bool ValidateNewScenePath()
	{
		if (!string.IsNullOrEmpty(newScenePath) && newScenePath.StartsWith("Assets/") && newScenePath.EndsWith(".unity"))
		{
			return true;
		}

		Debug.LogError($"MapEnvironmentSetup: Scene path must be under 'Assets/' and end with '.unity' (got '{newScenePath}').");
		return false;
	}
}
