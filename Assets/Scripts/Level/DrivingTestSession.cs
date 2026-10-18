using UnityEngine;

// Runs a playable course: places the test vehicle on the first route point and restarts the test on request.
[DisallowMultipleComponent]
[RequireComponent(typeof(DrivingTestLevel))]
public class DrivingTestSession : MonoBehaviour
{
	[Header("References")]
	[SerializeField] private DrivingTestLevel drivingTestLevel;
	[SerializeField] private MapGenerator mapGenerator;
	[SerializeField] private Rigidbody vehicleRigidbody;

	[Header("Spawn")]
	[Tooltip("Height of the vehicle root above the ground at the first route point, in meters.")]
	[SerializeField] private float spawnHeightAboveGround = 1f;
	[SerializeField] private bool startCourseOnPlay = true;

	[Header("Restart")]
	[SerializeField] private KeyCode restartKey = KeyCode.R;
	[Tooltip("Optional. While its menu is open, the restart key is ignored.")]
	[SerializeField] private MenuController menuController;

	public KeyCode RestartKey => restartKey;
	public Rigidbody VehicleRigidbody => vehicleRigidbody;

	// Start runs after every Awake, so DrivingTestLevel has already generated the route sections.
	private void Start()
	{
		CacheReferences();

		if (startCourseOnPlay)
		{
			RestartCourse();
		}
	}

	private void Update()
	{
		bool isMenuOpen = menuController != null && menuController.IsMenuOpen;
		if (!isMenuOpen && Input.GetKeyDown(restartKey))
		{
			RestartCourse();
		}
	}

	public void RestartCourse()
	{
		if (!CanStartCourse())
		{
			return;
		}

		MapRouteSection firstSection = mapGenerator.RouteSections[0];

		drivingTestLevel.ResetLevel();
		PlaceVehicle(GetStartPose(firstSection));
		drivingTestLevel.EnterFirstRoutePoint(vehicleRigidbody.gameObject);

		Debug.Log($"DrivingTestSession: Course restarted at Point {drivingTestLevel.CurrentPointNumber}.", this);
	}

	private bool CanStartCourse()
	{
		if (drivingTestLevel == null || mapGenerator == null || vehicleRigidbody == null)
		{
			Debug.LogWarning("DrivingTestSession: DrivingTestLevel, MapGenerator or vehicle Rigidbody reference is missing.", this);
			return false;
		}

		if (!string.IsNullOrEmpty(mapGenerator.RouteInstructionError))
		{
			Debug.LogWarning($"DrivingTestSession: Route data is invalid. {mapGenerator.RouteInstructionError}", this);
			return false;
		}

		if (mapGenerator.RouteSections.Count == 0 || mapGenerator.RouteSections[0] == null)
		{
			Debug.LogWarning("DrivingTestSession: The map has no route points to start from.", this);
			return false;
		}

		if (!mapGenerator.RouteSections[0].TryGetComponent<BoxCollider>(out _))
		{
			Debug.LogWarning("DrivingTestSession: The first route point has no trigger collider.", this);
			return false;
		}

		if (!drivingTestLevel.IsTestVehicle(vehicleRigidbody.gameObject))
		{
			Debug.LogWarning("DrivingTestSession: The vehicle is not the DrivingTestLevel test vehicle.", this);
			return false;
		}

		return true;
	}

	// World pose on the first route point, facing along the route direction.
	// The editor setup uses it too, so the vehicle is already at the start before Play.
	public Pose GetStartPose(MapRouteSection firstSection)
	{
		Transform firstPoint = firstSection.transform;
		BoxCollider firstPointTrigger = firstSection.GetComponent<BoxCollider>();

		Vector3 spawnPosition = firstPoint.position;
		spawnPosition.y = firstPointTrigger.bounds.min.y + spawnHeightAboveGround;

		Vector3 routeForward = Vector3.ProjectOnPlane(firstPoint.forward, Vector3.up);
		Quaternion spawnRotation = routeForward.sqrMagnitude > Mathf.Epsilon
			? Quaternion.LookRotation(routeForward, Vector3.up)
			: Quaternion.identity;

		return new Pose(spawnPosition, spawnRotation);
	}

	// Moves the vehicle to the start pose and clears its motion.
	private void PlaceVehicle(Pose startPose)
	{
		vehicleRigidbody.transform.SetPositionAndRotation(startPose.position, startPose.rotation);
		vehicleRigidbody.position = startPose.position;
		vehicleRigidbody.rotation = startPose.rotation;
		vehicleRigidbody.linearVelocity = Vector3.zero;
		vehicleRigidbody.angularVelocity = Vector3.zero;
	}

	private void CacheReferences()
	{
		if (drivingTestLevel == null)
		{
			TryGetComponent(out drivingTestLevel);
		}

		if (mapGenerator == null)
		{
			TryGetComponent(out mapGenerator);
		}
	}
}
