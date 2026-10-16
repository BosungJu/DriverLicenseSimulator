using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Makes the open level scene drivable: copies the vehicle from the vehicle scene and wires the test components.
public static class DrivingTestSceneSetup
{
	private const string MenuPath = "Tools/Driving Test/Set Up Drivable Level";
	private const string VehicleScenePath = "Assets/Scenes/MainSenes.unity";
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
			vehicle = CopyVehicleFromVehicleScene(levelScene);
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

		EditorSceneManager.MarkSceneDirty(levelScene);
		Selection.activeGameObject = levelObject;
		Debug.Log($"DrivingTestSceneSetup: '{levelScene.name}' is ready to drive with '{vehicle.name}'. Save the scene to keep the setup.", levelObject);
	}

	// Instantiate remaps references inside the copied hierarchy (cameras, effects) to the copy.
	private static CarController CopyVehicleFromVehicleScene(Scene levelScene)
	{
		Scene vehicleScene = SceneManager.GetSceneByPath(VehicleScenePath);
		bool wasVehicleSceneLoaded = vehicleScene.IsValid() && vehicleScene.isLoaded;
		if (!wasVehicleSceneLoaded)
		{
			vehicleScene = EditorSceneManager.OpenScene(VehicleScenePath, OpenSceneMode.Additive);
		}

		try
		{
			CarController sourceVehicle = FindInScene<CarController>(vehicleScene);
			if (sourceVehicle == null)
			{
				Debug.LogError($"DrivingTestSceneSetup: No CarController in '{VehicleScenePath}'.");
				return null;
			}

			GameObject vehicleCopy = Object.Instantiate(sourceVehicle.gameObject);
			vehicleCopy.name = sourceVehicle.gameObject.name;
			SceneManager.MoveGameObjectToScene(vehicleCopy, levelScene);
			Undo.RegisterCreatedObjectUndo(vehicleCopy, "Copy Driving Test Vehicle");

			return vehicleCopy.GetComponent<CarController>();
		}
		finally
		{
			// The vehicle scene is only read; closing it without saving leaves the file unchanged.
			if (!wasVehicleSceneLoaded)
			{
				EditorSceneManager.CloseScene(vehicleScene, true);
			}

			SceneManager.SetActiveScene(levelScene);
		}
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
