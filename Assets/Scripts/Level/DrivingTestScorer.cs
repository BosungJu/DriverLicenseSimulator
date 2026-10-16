using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class DrivingScoreChangedEvent : UnityEvent<int>
{
}

[System.Serializable]
public class DrivingDeductedEvent : UnityEvent<DrivingDeduction>
{
}

// Feeds the running course (section changes, vehicle speed, line contacts) into the scoring rules.
[DisallowMultipleComponent]
[RequireComponent(typeof(DrivingTestLevel))]
public class DrivingTestScorer : MonoBehaviour
{
	private const float MetersPerSecondToKmh = 3.6f;

	[Header("References")]
	[SerializeField] private DrivingTestLevel drivingTestLevel;
	[SerializeField] private MapGenerator mapGenerator;
	[Tooltip("Optional. When empty, the Rigidbody of the vehicle that entered the course is used.")]
	[SerializeField] private Rigidbody vehicleRigidbody;

	[SerializeField] private DrivingScoringSettings scoringSettings = new DrivingScoringSettings();

	[Header("Events")]
	[SerializeField] private DrivingScoreChangedEvent onScoreChanged = new DrivingScoreChangedEvent();
	[SerializeField] private DrivingDeductedEvent onDeducted = new DrivingDeductedEvent();

	private DrivingTestScoreEvaluator evaluator;
	private MapRouteSection scoredSection;
	private GameObject scoredVehicle;
	private Rigidbody scoredVehicleRigidbody;

	public DrivingScoreSheet ScoreSheet => evaluator.ScoreSheet;
	public int Score => evaluator.ScoreSheet.Score;
	public bool IsPassed => evaluator.ScoreSheet.IsPassed;
	public DrivingScoreChangedEvent OnScoreChanged => onScoreChanged;
	public DrivingDeductedEvent OnDeducted => onDeducted;

	private void Awake()
	{
		CacheReferences();
		evaluator = new DrivingTestScoreEvaluator(scoringSettings);
	}

	private void OnEnable()
	{
		CacheReferences();
		if (drivingTestLevel == null || mapGenerator == null)
		{
			Debug.LogWarning("DrivingTestScorer: DrivingTestLevel or MapGenerator reference is missing.", this);
			return;
		}

		evaluator.ScoreSheet.Deducted += HandleDeducted;
		mapGenerator.SubscribeLineCollision(HandleLineTouch);
		mapGenerator.SubscribeTestCollision(HandleLineTouch);
		drivingTestLevel.LevelReset += ResetScore;
		drivingTestLevel.OnLevelCompleted.AddListener(HandleLevelCompleted);
	}

	private void OnDisable()
	{
		evaluator.ScoreSheet.Deducted -= HandleDeducted;

		if (mapGenerator != null)
		{
			mapGenerator.UnsubscribeLineCollision(HandleLineTouch);
			mapGenerator.UnsubscribeTestCollision(HandleLineTouch);
		}

		if (drivingTestLevel != null)
		{
			drivingTestLevel.LevelReset -= ResetScore;
			drivingTestLevel.OnLevelCompleted.RemoveListener(HandleLevelCompleted);
		}
	}

	private void Update()
	{
		if (drivingTestLevel == null || drivingTestLevel.IsCompleted)
		{
			return;
		}

		SyncSection();

		Rigidbody vehicleBody = GetVehicleRigidbody();
		if (vehicleBody == null || scoredSection == null)
		{
			evaluator.Tick(Time.deltaTime, 0f);
			return;
		}

		float speedKmh = vehicleBody.linearVelocity.magnitude * MetersPerSecondToKmh;
		// Route points face the route direction, so this world-space projection grows as the vehicle advances.
		float routeDistanceMeters = Vector3.Dot(vehicleBody.position, scoredSection.transform.forward);
		evaluator.Tick(Time.deltaTime, speedKmh, routeDistanceMeters);
	}

	public void ResetScore()
	{
		evaluator.Reset();
		scoredSection = null;

		onScoreChanged.Invoke(evaluator.ScoreSheet.Score);
	}

	// Section entry happens in a physics callback, so the scorer catches up before using the section.
	private void SyncSection()
	{
		MapRouteSection currentSection = drivingTestLevel.CurrentSection;
		if (currentSection == null || currentSection == scoredSection)
		{
			return;
		}

		scoredSection = currentSection;
		evaluator.BeginSection(currentSection.PointNumber, currentSection.Commands);
	}

	private void HandleLineTouch(GameObject otherObject)
	{
		if (drivingTestLevel.IsCompleted || !drivingTestLevel.IsTestVehicle(otherObject))
		{
			return;
		}

		SyncSection();
		evaluator.RegisterLineTouch();
	}

	private void HandleLevelCompleted(GameObject vehicle)
	{
		SyncSection();
		evaluator.CompleteCourse();

		DrivingScoreSheet scoreSheet = evaluator.ScoreSheet;
		string result = scoreSheet.IsPassed ? "PASS" : "FAIL";
		Debug.Log($"DrivingTestScorer: Course completed with {scoreSheet.Score}/{scoreSheet.MaxScore} ({result}).", this);
	}

	private void HandleDeducted(DrivingDeduction deduction)
	{
		string penalty = deduction.IsDisqualification ? "disqualified" : $"-{deduction.Points}";
		Debug.Log($"DrivingTestScorer: {deduction.Reason} at Point {deduction.PointNumber} [{deduction.Command}] {penalty}, score {evaluator.ScoreSheet.Score}.", this);

		onDeducted.Invoke(deduction);
		onScoreChanged.Invoke(evaluator.ScoreSheet.Score);
	}

	// The active vehicle's Rigidbody is looked up once per vehicle rather than every frame.
	private Rigidbody GetVehicleRigidbody()
	{
		if (vehicleRigidbody != null)
		{
			return vehicleRigidbody;
		}

		GameObject activeVehicle = drivingTestLevel.ActiveVehicle;
		if (activeVehicle != scoredVehicle)
		{
			scoredVehicle = activeVehicle;
			scoredVehicleRigidbody = activeVehicle != null ? activeVehicle.GetComponentInParent<Rigidbody>() : null;
		}

		return scoredVehicleRigidbody;
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
