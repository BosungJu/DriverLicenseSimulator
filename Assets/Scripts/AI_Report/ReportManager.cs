using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Records the vehicle state during a course and, when the course ends, exports the samples and
// the per-section deductions for the Python pipeline (LSTM predict -> OpenAI report) in DriveLicenseSimulatorAIModel.
// Samples are taken every physics step, so the sample rate is 1 / Time.fixedDeltaTime and must match the
// sample_rate_hz the LSTM checkpoint was trained with.
// Run after CarController so control inputs belong to the same physics step as the sample.
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class ReportManager : MonoBehaviour
{
	private const float MetersPerSecondToKmh = 3.6f;
	private const string DrivingLogFileName = "driving_log.csv";
	private const string SectionResultFileName = "section_results.json";
	private const string SessionFolderFormat = "yyyyMMdd_HHmmss_fff";

	// Encoding.UTF8 writes a BOM, which Python's json module rejects with the plain utf-8 codec.
	private static readonly Encoding ExportEncoding = new UTF8Encoding(false);

	[Header("References")]
	[SerializeField] private DrivingTestLevel drivingTestLevel;
	[SerializeField] private DrivingTestScorer drivingTestScorer;
	[SerializeField] private MapGenerator mapGenerator;
	[Tooltip("Optional. When empty, the Rigidbody of the vehicle that entered the course is used.")]
	[SerializeField] private Rigidbody vehicleRigidbody;

	[Header("Export")]
	[Tooltip("Folder under Application.persistentDataPath where each session folder is created.")]
	[SerializeField] private string outputFolderName = "AIReport";

	private readonly List<DrivingSample> samples = new List<DrivingSample>();
	private float recordedSeconds;
	private float previousForwardSpeedMps;
	private bool hasPreviousSample;
	private bool isExportPending;
	private GameObject recordedVehicle;
	private Rigidbody recordedVehicleRigidbody;
	private Rigidbody inputSourceRigidbody;
	private CarController recordedVehicleController;
	private MapRouteSection featureSourceSection;
	private DrivingSectionTraits currentSectionTraits;
	private bool isCurrentSectionFinishApproach;

	public IReadOnlyList<DrivingSample> Samples => samples;
	public string LastExportDirectory { get; private set; }

	public event Action<string> ReportDataExported;

	// Fills missing references from this GameObject before other components start using them.
	private void Awake()
	{
		CacheReferences();
	}

	// Subscribes to course reset and completion; recording stays inactive when a required reference is missing.
	private void OnEnable()
	{
		CacheReferences();
		if (drivingTestLevel == null || drivingTestScorer == null || mapGenerator == null)
		{
			Debug.LogWarning("ReportManager: DrivingTestLevel, DrivingTestScorer or MapGenerator reference is missing.", this);
			return;
		}

		drivingTestLevel.LevelReset += ResetRecording;
		drivingTestLevel.OnLevelCompleted.AddListener(HandleLevelCompleted);
	}

	// Unsubscribes the course listeners added in OnEnable.
	private void OnDisable()
	{
		if (drivingTestLevel != null)
		{
			drivingTestLevel.LevelReset -= ResetRecording;
			drivingTestLevel.OnLevelCompleted.RemoveListener(HandleLevelCompleted);
		}
	}

	// Physics steps give the LSTM a fixed sample rate regardless of the rendering frame rate.
	private void FixedUpdate()
	{
		// The scorer and map are needed for the section features; without them OnEnable never subscribes to the export.
		if (drivingTestLevel == null || drivingTestScorer == null || mapGenerator == null ||
			drivingTestLevel.IsCompleted || drivingTestLevel.CurrentSection == null)
		{
			return;
		}

		UpdateSectionFeatures(drivingTestLevel.CurrentSection);

		Rigidbody vehicleBody = GetVehicleRigidbody();
		if (vehicleBody == null)
		{
			hasPreviousSample = false;
			recordedSeconds += Time.fixedDeltaTime;
			return;
		}

		CarController vehicleController = GetVehicleController(vehicleBody);
		VehicleSystems vehicleSystems = vehicleController != null ? vehicleController.systems : null;
		if (vehicleSystems != null && !vehicleSystems.isActiveAndEnabled)
		{
			vehicleSystems = null;
		}

		if (vehicleController != null && (!vehicleController.isActiveAndEnabled ||
			!vehicleController.HasInputSample || vehicleController.InputSampleFixedTime != Time.fixedTime))
		{
			vehicleController = null;
		}

		RecordSample(vehicleBody, vehicleController, vehicleSystems, drivingTestLevel.CurrentSection.PointNumber, Time.fixedDeltaTime);
		recordedSeconds += Time.fixedDeltaTime;
	}

	// The scorer applies course-end deductions in its own OnLevelCompleted listener, and listener order is
	// not guaranteed, so the export waits until every listener of this frame has run.
	private void LateUpdate()
	{
		if (!isExportPending)
		{
			return;
		}

		isExportPending = false;
		ExportReportData();
	}

	// Clears recorded samples and timing state, and cancels a pending export, so a new course starts from zero.
	public void ResetRecording()
	{
		samples.Clear();
		recordedSeconds = 0f;
		previousForwardSpeedMps = 0f;
		hasPreviousSample = false;
		isExportPending = false;
		featureSourceSection = null;
	}

	// Only marks the export as pending; LateUpdate performs it after the scorer's final deductions.
	private void HandleLevelCompleted(GameObject vehicle)
	{
		isExportPending = true;
	}

	// Vehicle systems toggle in Update, so a sample holds the latest state set before this physics step.
	private void RecordSample(Rigidbody vehicleBody, CarController vehicleController, VehicleSystems vehicleSystems,
		int pointNumber, float stepSeconds)
	{
		Vector3 velocity = vehicleBody.linearVelocity;
		float forwardSpeedMps = Vector3.Dot(velocity, vehicleBody.transform.forward);
		float accelerationMps2 = hasPreviousSample
			? (forwardSpeedMps - previousForwardSpeedMps) / stepSeconds
			: 0f;

		samples.Add(new DrivingSample
		{
			TimeSeconds = recordedSeconds,
			PointNumber = pointNumber,
			SpeedKmh = velocity.magnitude * MetersPerSecondToKmh,
			LongitudinalAccelerationMps2 = accelerationMps2,
			YawRateDegreesPerSecond = vehicleBody.angularVelocity.y * Mathf.Rad2Deg,
			HeadingDegrees = vehicleBody.rotation.eulerAngles.y,
			PositionX = vehicleBody.position.x,
			PositionZ = vehicleBody.position.z,
			HasControlInput = vehicleController != null,
			SteeringInput = vehicleController != null ? vehicleController.SteeringInput : 0f,
			ThrottleInput = vehicleController != null ? vehicleController.ThrottleInput : 0f,
			BrakeInput = vehicleController != null ? vehicleController.BrakeInput : 0f,
			Gear = vehicleController != null ? vehicleController.CurrentGear : VehicleGear.Park,
			IsParkingBrakeOn = vehicleController != null && vehicleController.ParkingBrakeOn,
			HasVehicleSystemState = vehicleSystems != null,
			IsEngineOn = vehicleSystems != null && vehicleSystems.EngineOn,
			IsLeftSignalOn = vehicleSystems != null && vehicleSystems.LeftSignalOn,
			IsRightSignalOn = vehicleSystems != null && vehicleSystems.RightSignalOn,
			IsHazardOn = vehicleSystems != null && vehicleSystems.HazardOn,
			IsHeadlightOn = vehicleSystems != null && vehicleSystems.HeadlightOn,
			IsHighBeamOn = vehicleSystems != null && vehicleSystems.HighBeamOn,
			IsSeatBeltOn = vehicleSystems != null && vehicleSystems.SeatBeltOn,
			IsHornOn = vehicleSystems != null && vehicleSystems.HornOn,
			SectionTraits = currentSectionTraits,
			IsFinishApproach = isCurrentSectionFinishApproach
		});

		previousForwardSpeedMps = forwardSpeedMps;
		hasPreviousSample = true;
	}

	// Writes driving_log.csv and section_results.json into a new timestamped session folder, then raises
	// ReportDataExported with that folder. On an IO or permission failure it logs an error and raises nothing.
	private void ExportReportData()
	{
		string sessionDirectory = Path.Combine(Application.persistentDataPath, outputFolderName,
			DateTime.Now.ToString(SessionFolderFormat, CultureInfo.InvariantCulture));

		try
		{
			Directory.CreateDirectory(sessionDirectory);
			File.WriteAllText(Path.Combine(sessionDirectory, DrivingLogFileName), DrivingReportCsv.Build(samples), ExportEncoding);

			string sectionResultJson = JsonUtility.ToJson(BuildSectionResultFile(), true);
			File.WriteAllText(Path.Combine(sessionDirectory, SectionResultFileName), sectionResultJson, ExportEncoding);
		}
		catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
		{
			Debug.LogError($"ReportManager: Failed to export report data to {sessionDirectory}. {exception.Message}", this);
			return;
		}

		LastExportDirectory = sessionDirectory;
		Debug.Log($"ReportManager: Exported {samples.Count} samples and section results to {sessionDirectory}.", this);
		ReportDataExported?.Invoke(sessionDirectory);
	}

	// Combines the final score sheet, the route sections with their sample counts, and every deduction
	// into the section_results.json payload.
	private DrivingSectionResultFile BuildSectionResultFile()
	{
		DrivingScoreSheet scoreSheet = drivingTestScorer.ScoreSheet;
		var resultFile = new DrivingSectionResultFile
		{
			recordedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
			score = scoreSheet.Score,
			maxScore = scoreSheet.MaxScore,
			passingScore = scoreSheet.PassingScore,
			totalDeduction = scoreSheet.TotalDeduction,
			isPassed = scoreSheet.IsPassed,
			isDisqualified = scoreSheet.IsDisqualified,
			recordedSeconds = recordedSeconds,
			sampleRateHz = 1f / Time.fixedDeltaTime
		};

		foreach (MapRouteSection routeSection in mapGenerator.RouteSections)
		{
			if (routeSection == null)
			{
				continue;
			}

			resultFile.sections.Add(new DrivingSectionInfo
			{
				pointNumber = routeSection.PointNumber,
				instructionText = routeSection.InstructionText,
				commands = new List<string>(routeSection.Commands),
				sampleCount = CountSamples(routeSection.PointNumber)
			});
		}

		foreach (DrivingDeduction deduction in scoreSheet.Deductions)
		{
			resultFile.deductions.Add(new DrivingDeductionRecord
			{
				pointNumber = deduction.PointNumber,
				command = deduction.Command,
				reason = deduction.Reason.ToString(),
				points = deduction.Points,
				isDisqualification = deduction.IsDisqualification
			});
		}

		return resultFile;
	}

	// Section features only change on section entry, so they are derived once per section with the scorer's settings.
	// The finish point completes the course on entry, so its turn signal rule judges the section before it.
	private void UpdateSectionFeatures(MapRouteSection currentSection)
	{
		if (currentSection == featureSourceSection)
		{
			return;
		}

		featureSourceSection = currentSection;
		int hillStopStep = drivingTestScorer.ScoringSettings.HillStopStep;
		currentSectionTraits = DrivingSectionTraits.FromCommands(currentSection.Commands, hillStopStep);

		IReadOnlyList<MapRouteSection> routeSections = mapGenerator.RouteSections;
		int nextRouteIndex = currentSection.RouteIndex + 1;
		MapRouteSection nextSection = nextRouteIndex < routeSections.Count ? routeSections[nextRouteIndex] : null;
		isCurrentSectionFinishApproach = nextSection != null &&
			DrivingSectionTraits.FromCommands(nextSection.Commands, hillStopStep).IsFinish;
	}

	// Returns how many recorded samples belong to the section with the given point number.
	private int CountSamples(int pointNumber)
	{
		int sampleCount = 0;
		foreach (DrivingSample sample in samples)
		{
			if (sample.PointNumber == pointNumber)
			{
				sampleCount++;
			}
		}

		return sampleCount;
	}

	// The active vehicle's Rigidbody is looked up once per vehicle rather than every frame.
	private Rigidbody GetVehicleRigidbody()
	{
		if (vehicleRigidbody != null)
		{
			return vehicleRigidbody;
		}

		GameObject activeVehicle = drivingTestLevel.ActiveVehicle;
		if (activeVehicle != recordedVehicle)
		{
			recordedVehicle = activeVehicle;
			recordedVehicleRigidbody = activeVehicle != null ? activeVehicle.GetComponentInParent<Rigidbody>() : null;
		}

		return recordedVehicleRigidbody;
	}

	// Looks up the CarController again only when the vehicle Rigidbody changes, and warns once per vehicle
	// when control input or vehicle system state will be missing from the samples.
	private CarController GetVehicleController(Rigidbody vehicleBody)
	{
		if (inputSourceRigidbody != vehicleBody)
		{
			inputSourceRigidbody = vehicleBody;
			recordedVehicleController = vehicleBody.GetComponentInParent<CarController>();
			if (recordedVehicleController == null)
			{
				Debug.LogWarning("ReportManager: CarController is missing; only physics state will be exported.", this);
			}
			else if (recordedVehicleController.systems == null)
			{
				Debug.LogWarning("ReportManager: CarController.systems is missing; vehicle system state will be blank.", this);
			}
		}

		return recordedVehicleController;
	}

	// Keeps references assigned in the Inspector and fills empty ones from components on this GameObject.
	private void CacheReferences()
	{
		if (drivingTestLevel == null)
		{
			TryGetComponent(out drivingTestLevel);
		}

		if (drivingTestScorer == null)
		{
			TryGetComponent(out drivingTestScorer);
		}

		if (mapGenerator == null)
		{
			TryGetComponent(out mapGenerator);
		}
	}
}
