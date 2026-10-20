using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Makes the open level scene drivable: places the vehicle prefab on the course start and wires the test components.
public static class DrivingTestSceneSetup
{
	private const string MenuPath = "Tools/Driving Test/Set Up Drivable Level";
	private const string VehiclePrefabPath = "Assets/Resources/Car/Prefabs/Car.prefab";
	private const string SceneCameraName = "Main Camera";

	[MenuItem(MenuPath)]
	private static void SetUpDrivableLevel()
	{
		Scene levelScene = SceneManager.GetActiveScene();
		DrivingTestLevel drivingTestLevel = FindInScene<DrivingTestLevel>(levelScene);
		if (drivingTestLevel == null)
		{
			Debug.LogError($"DrivingTestSceneSetup: No DrivingTestLevel in the active scene '{levelScene.name}'.");
			return;
		}

		CarController vehicle = FindInScene<CarController>(levelScene);
		if (vehicle == null)
		{
			vehicle = InstantiateVehiclePrefab(levelScene);
			if (vehicle == null)
			{
				return;
			}
		}

		Rigidbody vehicleRigidbody = vehicle.GetComponent<Rigidbody>();
		if (vehicleRigidbody == null)
		{
			Debug.LogError($"DrivingTestSceneSetup: Vehicle '{vehicle.name}' has no Rigidbody.", vehicle);
			return;
		}

		GameObject levelObject = drivingTestLevel.gameObject;
		MapGenerator mapGenerator = levelObject.GetComponent<MapGenerator>();
		DrivingTestScorer drivingTestScorer = GetOrAddComponent<DrivingTestScorer>(levelObject);
		DrivingTestSession drivingTestSession = GetOrAddComponent<DrivingTestSession>(levelObject);
		DrivingTestHud drivingTestHud = GetOrAddComponent<DrivingTestHud>(levelObject);

		AssignReference(drivingTestLevel, "testVehicle", vehicle.gameObject);
		AssignReference(drivingTestScorer, "drivingTestLevel", drivingTestLevel);
		AssignReference(drivingTestScorer, "mapGenerator", mapGenerator);
		AssignReference(drivingTestScorer, "vehicleRigidbody", vehicleRigidbody);
		AssignReference(drivingTestSession, "drivingTestLevel", drivingTestLevel);
		AssignReference(drivingTestSession, "mapGenerator", mapGenerator);
		AssignReference(drivingTestSession, "vehicleRigidbody", vehicleRigidbody);
		AssignReference(drivingTestHud, "drivingTestLevel", drivingTestLevel);
		AssignReference(drivingTestHud, "drivingTestScorer", drivingTestScorer);
		AssignReference(drivingTestHud, "drivingTestSession", drivingTestSession);
		AssignReference(drivingTestHud, "mapGenerator", mapGenerator);

		DisableSceneCamera(levelScene, vehicle.transform);
		PlaceVehicleAtStart(vehicle.transform, drivingTestLevel, mapGenerator, drivingTestSession);

		EditorSceneManager.MarkSceneDirty(levelScene);
		Selection.activeGameObject = levelObject;
		Debug.Log($"DrivingTestSceneSetup: '{levelScene.name}' is ready to drive with '{vehicle.name}'. Save the scene to keep the setup.", levelObject);
	}

	// A prefab instance keeps its link, so later edits to Car.prefab reach every level scene.
	private static CarController InstantiateVehiclePrefab(Scene levelScene)
	{
		GameObject vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath);
		if (vehiclePrefab == null)
		{
			Debug.LogError($"DrivingTestSceneSetup: No vehicle prefab at '{VehiclePrefabPath}'.");
			return null;
		}

		if (!vehiclePrefab.TryGetComponent(out CarController _))
		{
			Debug.LogError($"DrivingTestSceneSetup: Vehicle prefab '{VehiclePrefabPath}' has no CarController on its root.", vehiclePrefab);
			return null;
		}

		var vehicleInstance = (GameObject)PrefabUtility.InstantiatePrefab(vehiclePrefab, levelScene);
		Undo.RegisterCreatedObjectUndo(vehicleInstance, "Place Driving Test Vehicle");

		return vehicleInstance.GetComponent<CarController>();
	}

	// Uses the same start pose as DrivingTestSession at Play, so the scene view shows where the test begins.
	private static void PlaceVehicleAtStart(Transform vehicleRoot, DrivingTestLevel drivingTestLevel, MapGenerator mapGenerator, DrivingTestSession drivingTestSession)
	{
		MapRouteSection firstSection = FindFirstRouteSection(drivingTestLevel, mapGenerator);
		if (firstSection == null || !firstSection.TryGetComponent(out BoxCollider _))
		{
			Debug.LogWarning("DrivingTestSceneSetup: No first route point with a trigger, so the vehicle was not moved. Check the map CSV and run Set Up again.", drivingTestLevel);
			return;
		}

		// Collider bounds come from the physics scene, which is not synced with edit-time transform changes.
		Physics.SyncTransforms();
		Pose startPose = drivingTestSession.GetStartPose(firstSection);

		Undo.RecordObject(vehicleRoot, "Place Vehicle At Course Start");
		vehicleRoot.SetPositionAndRotation(startPose.position, startPose.rotation);
		PrefabUtility.RecordPrefabInstancePropertyModifications(vehicleRoot);
	}

	// Route sections in the scene survive a domain reload, but MapGenerator's runtime list does not,
	// so the saved components are checked before generating the map.
	private static MapRouteSection FindFirstRouteSection(DrivingTestLevel drivingTestLevel, MapGenerator mapGenerator)
	{
		MapRouteSection savedFirstSection = FindRouteSectionByIndex(drivingTestLevel, 0);
		if (savedFirstSection != null)
		{
			return savedFirstSection;
		}

		if (mapGenerator == null)
		{
			return null;
		}

		Debug.Log("DrivingTestSceneSetup: No route points in the scene, generating the map.", mapGenerator);
		mapGenerator.GenerateMap();

		return FindRouteSectionByIndex(drivingTestLevel, 0);
	}

	private static MapRouteSection FindRouteSectionByIndex(DrivingTestLevel drivingTestLevel, int routeIndex)
	{
		foreach (MapRouteSection section in drivingTestLevel.GetComponentsInChildren<MapRouteSection>(true))
		{
			if (section.RouteIndex == routeIndex)
			{
				return section;
			}
		}

		return null;
	}

	// The vehicle carries its own driver cameras, so the overview camera of the level scene is turned off.
	private static void DisableSceneCamera(Scene levelScene, Transform vehicleRoot)
	{
		foreach (Camera sceneCamera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
		{
			bool isLevelCamera = sceneCamera.gameObject.scene == levelScene
				&& sceneCamera.name == SceneCameraName
				&& !sceneCamera.transform.IsChildOf(vehicleRoot);
			if (!isLevelCamera || !sceneCamera.gameObject.activeSelf)
			{
				continue;
			}

			Undo.RecordObject(sceneCamera.gameObject, "Disable Level Scene Camera");
			sceneCamera.gameObject.SetActive(false);
		}
	}

	private static T FindInScene<T>(Scene scene) where T : Component
	{
		foreach (GameObject rootObject in scene.GetRootGameObjects())
		{
			T component = rootObject.GetComponentInChildren<T>(true);
			if (component != null)
			{
				return component;
			}
		}

		return null;
	}

	private static T GetOrAddComponent<T>(GameObject target) where T : Component
	{
		if (target.TryGetComponent(out T component))
		{
			return component;
		}

		return Undo.AddComponent<T>(target);
	}

	private static void AssignReference(Object target, string propertyName, Object reference)
	{
		var serializedTarget = new SerializedObject(target);
		SerializedProperty property = serializedTarget.FindProperty(propertyName);
		if (property == null)
		{
			Debug.LogError($"DrivingTestSceneSetup: '{target.GetType().Name}' has no serialized field '{propertyName}'.", target);
			return;
		}

		property.objectReferenceValue = reference;
		serializedTarget.ApplyModifiedProperties();
	}
}
