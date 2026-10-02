using UnityEngine;

// Development HUD for trying the course: speed, current point and commands, score and the latest deduction.
[DisallowMultipleComponent]
public class DrivingTestHud : MonoBehaviour
{
	private const float MetersPerSecondToKmh = 3.6f;
	private const int FontSize = 22;
	private const float PanelMargin = 16f;
	private const float PanelWidth = 420f;
	private const float PanelHeight = 230f;

	[Header("References")]
	[SerializeField] private DrivingTestLevel drivingTestLevel;
	[SerializeField] private DrivingTestScorer drivingTestScorer;
	[SerializeField] private DrivingTestSession drivingTestSession;
	[SerializeField] private MapGenerator mapGenerator;

	private DrivingDeduction lastDeduction;
	private GUIStyle panelStyle;

	private void Awake()
	{
		CacheReferences();
	}

	private void OnEnable()
	{
		CacheReferences();
		if (drivingTestLevel == null || drivingTestScorer == null)
		{
			Debug.LogWarning("DrivingTestHud: DrivingTestLevel or DrivingTestScorer reference is missing.", this);
			return;
		}

		drivingTestScorer.OnDeducted.AddListener(HandleDeducted);
		drivingTestLevel.LevelReset += ClearLastDeduction;
	}

	private void OnDisable()
	{
		if (drivingTestScorer != null)
		{
			drivingTestScorer.OnDeducted.RemoveListener(HandleDeducted);
		}

		if (drivingTestLevel != null)
		{
			drivingTestLevel.LevelReset -= ClearLastDeduction;
		}
	}

	private void OnGUI()
	{
		if (drivingTestLevel == null || drivingTestScorer == null)
		{
			return;
		}

		if (panelStyle == null)
		{
			panelStyle = new GUIStyle(GUI.skin.box)
			{
				alignment = TextAnchor.UpperLeft,
				fontSize = FontSize,
				richText = true,
				padding = new RectOffset(12, 12, 10, 10)
			};
		}

		var panelRect = new Rect(PanelMargin, PanelMargin, PanelWidth, PanelHeight);
		GUI.Box(panelRect, BuildStatusText(), panelStyle);
	}

	private string BuildStatusText()
	{
		DrivingScoreSheet scoreSheet = drivingTestScorer.ScoreSheet;
		var status = new System.Text.StringBuilder();

		status.AppendLine($"Speed  {GetVehicleSpeedKmh():0} km/h");
		status.AppendLine($"Point  {FormatCurrentPoint()}");
		status.AppendLine($"Command  {FormatCurrentInstructions()}");
		status.AppendLine($"Score  {scoreSheet.Score}/{scoreSheet.MaxScore}");
		status.AppendLine($"Last  {FormatDeduction(lastDeduction)}");

		if (drivingTestLevel.IsCompleted)
		{
			bool isTestPassed = scoreSheet.IsPassed;
			string resultColor = isTestPassed ? "lime" : "red";
			string result = isTestPassed ? "PASS" : "FAIL";
			status.AppendLine($"<color={resultColor}><b>Course completed: {result}</b></color>");
		}
		else if (scoreSheet.IsDisqualified)
		{
			status.AppendLine($"<color=red><b>Disqualified: {scoreSheet.Disqualification.Reason}</b></color>");
		}

		if (drivingTestSession != null)
		{
			status.Append($"[{drivingTestSession.RestartKey}] Restart");
		}

		return status.ToString();
	}

	private float GetVehicleSpeedKmh()
	{
		if (drivingTestSession == null || drivingTestSession.VehicleRigidbody == null)
		{
			return 0f;
		}

		return drivingTestSession.VehicleRigidbody.linearVelocity.magnitude * MetersPerSecondToKmh;
	}

	private string FormatCurrentPoint()
	{
		int routePointCount = mapGenerator != null ? mapGenerator.RoutePointCount : 0;
		if (drivingTestLevel.CurrentPointNumber == DrivingTestLevel.NoCurrentPoint)
		{
			return $"- / {routePointCount}";
		}

		return $"{drivingTestLevel.CurrentPointNumber} ({drivingTestLevel.PassedRoutePointCount}/{routePointCount})";
	}

	private string FormatCurrentInstructions()
	{
		if (drivingTestLevel.CurrentInstructions.Count == 0)
		{
			return "-";
		}

		return string.Join(" | ", drivingTestLevel.CurrentInstructions);
	}

	private static string FormatDeduction(DrivingDeduction deduction)
	{
		if (deduction == null)
		{
			return "-";
		}

		string penalty = deduction.IsDisqualification ? "DQ" : $"-{deduction.Points}";
		return $"{deduction.Reason} {penalty} (P{deduction.PointNumber})";
	}

	private void HandleDeducted(DrivingDeduction deduction)
	{
		lastDeduction = deduction;
	}

	private void ClearLastDeduction()
	{
		lastDeduction = null;
	}

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

		if (drivingTestSession == null)
		{
			TryGetComponent(out drivingTestSession);
		}

		if (mapGenerator == null)
		{
			TryGetComponent(out mapGenerator);
		}
	}
}
