using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Runs a playable course: places the test vehicle on the first route point and restarts the test on request.
// In section practice it repeats only part of the course: every completed attempt is exported like a course
// (report and training data) and the vehicle returns to the practice start for the next attempt.
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

	[Header("Section Practice")]
	[Tooltip("Repeats part of the course to collect data of chosen sections instead of running the whole course.")]
	[SerializeField] private bool isSectionPracticeEnabled;
	[Tooltip("CAD point number where every attempt starts; the section of this point is driven first.")]
	[SerializeField] private int practiceStartPointNumber;
	[Tooltip("Consecutive sections per attempt. Cover every T_TEST step when practicing parking, " +
		"and start one point earlier to enter the target section at driving speed.")]
	[Min(1)]
	[SerializeField] private int practiceSectionCount = 1;
	[Tooltip("Seconds between a completed (and saved) attempt and the next attempt.")]
	[Min(0f)]
	[SerializeField] private float practiceRestartDelaySeconds = 2f;

	private Coroutine practiceRestartCoroutine;

	public KeyCode RestartKey => restartKey;
	public Rigidbody VehicleRigidbody => vehicleRigidbody;
	public bool IsSectionPracticeEnabled => isSectionPracticeEnabled;

	private void OnEnable()
	{
		CacheReferences();
		if (drivingTestLevel != null)
		{
			drivingTestLevel.OnLevelCompleted.AddListener(HandleLevelCompleted);
		}
	}

	private void OnDisable()
	{
		if (drivingTestLevel != null)
		{
			drivingTestLevel.OnLevelCompleted.RemoveListener(HandleLevelCompleted);
		}

		StopPracticeRestart();
	}

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

	// Restarts the whole course, or the practice range while section practice is enabled.
	public void RestartCourse()
	{
		StopPracticeRestart();
		if (!CanStartCourse())
		{
			return;
		}

		if (isSectionPracticeEnabled)
		{
			RestartPractice();
			return;
		}

		MapRouteSection firstSection = mapGenerator.RouteSections[0];

		drivingTestLevel.ResetLevel();
		PlaceVehicle(GetStartPose(firstSection));
		drivingTestLevel.EnterFirstRoutePoint(vehicleRigidbody.gameObject);

		Debug.Log($"DrivingTestSession: Course restarted at Point {drivingTestLevel.CurrentPointNumber}.", this);
	}

	// Vehicle controls (engine, seat belt, gear) are not reset, so later attempts start ready to drive.
	private void RestartPractice()
	{
		if (!TryGetPracticeRange(out int startRouteIndex, out int endRouteIndex))
		{
			return;
		}

		MapRouteSection startSection = mapGenerator.RouteSections[startRouteIndex];

		drivingTestLevel.ResetLevel();
		PlaceVehicle(GetStartPose(startSection));
		drivingTestLevel.EnterPracticeRoutePoint(startRouteIndex, endRouteIndex, vehicleRigidbody.gameObject);

		Debug.Log($"DrivingTestSession: Section practice restarted at Point {startSection.PointNumber} " +
			$"for {endRouteIndex - startRouteIndex} section(s).", this);
	}

	// Finds the route index of the practice start point; the end index is the route point that ends an attempt,
	// clamped to the last route point, which has no section of its own to drive.
	private bool TryGetPracticeRange(out int startRouteIndex, out int endRouteIndex)
	{
		IReadOnlyList<MapRouteSection> routeSections = mapGenerator.RouteSections;
		int lastRouteIndex = routeSections.Count - 1;
		startRouteIndex = FindRouteIndex(practiceStartPointNumber);
		endRouteIndex = Mathf.Min(startRouteIndex + Mathf.Max(1, practiceSectionCount), lastRouteIndex);

		if (startRouteIndex < 0)
		{
			Debug.LogWarning($"DrivingTestSession: Practice start Point {practiceStartPointNumber} is not on the route.", this);
			return false;
		}

		if (startRouteIndex >= lastRouteIndex)
		{
			Debug.LogWarning($"DrivingTestSession: Practice start Point {practiceStartPointNumber} is the finish point, " +
				"which has no section to drive.", this);
			return false;
		}

		if (!routeSections[startRouteIndex].TryGetComponent<BoxCollider>(out _))
		{
			Debug.LogWarning($"DrivingTestSession: Practice start Point {practiceStartPointNumber} has no trigger collider.", this);
			return false;
		}

		return true;
	}

	private int FindRouteIndex(int pointNumber)
	{
		IReadOnlyList<MapRouteSection> routeSections = mapGenerator.RouteSections;
		for (int i = 0; i < routeSections.Count; i++)
		{
			if (routeSections[i] != null && routeSections[i].PointNumber == pointNumber)
			{
				return i;
			}
		}

		return -1;
	}

	private void HandleLevelCompleted(GameObject vehicle)
	{
		if (!isSectionPracticeEnabled || !drivingTestLevel.IsPracticeRun)
		{
			return;
		}

		StopPracticeRestart();
		practiceRestartCoroutine = StartCoroutine(RestartPracticeAfterDelay());
	}

	// ReportManager exports the attempt in LateUpdate of the completion frame, and a restart clears its recording,
	// so the next attempt waits at least one frame even without a delay.
	private IEnumerator RestartPracticeAfterDelay()
	{
		yield return null;
		if (practiceRestartDelaySeconds > 0f)
		{
			yield return new WaitForSeconds(practiceRestartDelaySeconds);
		}

		practiceRestartCoroutine = null;
		RestartCourse();
	}

	private void StopPracticeRestart()
	{
		if (practiceRestartCoroutine == null)
		{
			return;
		}

		StopCoroutine(practiceRestartCoroutine);
		practiceRestartCoroutine = null;
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

	// World pose on a route point (the first one, or the practice start), facing along the route direction.
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
