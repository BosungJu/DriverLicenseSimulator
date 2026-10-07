using System;
using System.Collections.Generic;

// Applies the course test scoring table to section, speed, and line-contact input.
// Time only advances through Tick, so the rules are deterministic and testable without a scene.
public sealed class DrivingTestScoreEvaluator
{
	private readonly DrivingScoringSettings settings;

	private bool isCourseRunning;
	private bool hasSection;
	private int currentPointNumber;
	private IReadOnlyList<string> currentCommands = Array.Empty<string>();
	private bool isStartSection;
	private bool isParkingSection;
	private bool isAccelerationSection;
	private bool isEmergencySection;
	private bool isHillStopSection;
	private bool isSignalSection;

	private float courseElapsedSeconds;
	private float sectionElapsedSeconds;
	private float parkingElapsedSeconds;
	private float overSpeedElapsedSeconds;
	private float sectionMaxSpeedKmh;
	private float lastLineTouchSeconds;
	private int courseTimeOverCount;
	private int parkingTimeOverCount;
	private int overSpeedCount;
	private bool isEmergencyJudged;

	// Hill stop zone state; route distance is measured along the section's route direction.
	private bool hasHillStopped;
	private float hillStopRouteDistance;
	private float hillStopSeconds;
	private float hillElapsedSinceStopSeconds;
	private bool isHillDepartureJudged;
	private bool isHillRollbackPenalized;

	private float signalStopSeconds;
	private bool isSignalStopPenalized;

	public DrivingScoreSheet ScoreSheet { get; }
	public bool IsCourseRunning => isCourseRunning;
	public float CourseElapsedSeconds => courseElapsedSeconds;

	public DrivingTestScoreEvaluator(DrivingScoringSettings settings)
	{
		this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
		ScoreSheet = new DrivingScoreSheet(settings.MaxScore, settings.PassingScore);
		Reset();
	}

	public void Reset()
	{
		ScoreSheet.Reset();
		isCourseRunning = false;
		ClearSection();
		courseElapsedSeconds = 0f;
		parkingElapsedSeconds = 0f;
		overSpeedElapsedSeconds = 0f;
		lastLineTouchSeconds = float.NegativeInfinity;
		courseTimeOverCount = 0;
		parkingTimeOverCount = 0;
		overSpeedCount = 0;
	}

	// The course starts with the first section, so time before the start point is not scored.
	public void BeginSection(int pointNumber, IReadOnlyList<string> commands)
	{
		if (hasSection)
		{
			EndSection();
		}

		isCourseRunning = true;
		hasSection = true;
		currentPointNumber = pointNumber;
		currentCommands = commands ?? Array.Empty<string>();
		isStartSection = HasCommand(currentCommands, DrivingScoringSettings.StartCommand);
		isParkingSection = HasCommand(currentCommands, DrivingScoringSettings.ParkingCommand);
		isAccelerationSection = HasCommand(currentCommands, DrivingScoringSettings.AccelerationCommand);
		isEmergencySection = HasCommand(currentCommands, DrivingScoringSettings.EmergencyCommand);
		isHillStopSection = HasCommandStep(currentCommands, DrivingScoringSettings.HillCommand, settings.HillStopStep);
		isSignalSection = HasCommand(currentCommands, DrivingScoringSettings.SignalCommand);
		sectionElapsedSeconds = 0f;
		sectionMaxSpeedKmh = 0f;
		isEmergencyJudged = false;

		// The speed limit excludes the acceleration section, so an over-speed run does not carry into or out of it.
		if (isAccelerationSection)
		{
			ResetOverSpeed();
		}
	}

	public void EndSection()
	{
		if (!hasSection)
		{
			return;
		}

		if (isAccelerationSection && sectionMaxSpeedKmh < settings.AccelerationTargetKmh)
		{
			Deduct(DrivingDeductionReason.AccelerationTooSlow, settings.AccelerationPenalty,
				DrivingScoringSettings.AccelerationCommand);
		}

		if (isHillStopSection && !hasHillStopped)
		{
			Disqualify(DrivingDeductionReason.HillNotStopped, DrivingScoringSettings.HillCommand);
		}

		ClearSection();
	}

	public void CompleteCourse()
	{
		EndSection();
		isCourseRunning = false;
	}

	// routeDistanceMeters is the vehicle position along the current section's route direction (larger = further ahead).
	public void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters = 0f)
	{
		if (!isCourseRunning || !hasSection || deltaSeconds <= 0f)
		{
			return;
		}

		courseElapsedSeconds += deltaSeconds;
		sectionElapsedSeconds += deltaSeconds;
		sectionMaxSpeedKmh = Math.Max(sectionMaxSpeedKmh, speedKmh);

		JudgeStartTime();
		JudgeParkingTime(deltaSeconds);
		JudgeOverSpeed(deltaSeconds, speedKmh);
		JudgeEmergencyStop(speedKmh);
		JudgeHillStop(deltaSeconds, speedKmh, routeDistanceMeters);
		JudgeSignal(deltaSeconds, speedKmh);
		JudgeCourseTime();
	}

	public void RegisterLineTouch()
	{
		if (!isCourseRunning || !hasSection)
		{
			return;
		}

		if (courseElapsedSeconds - lastLineTouchSeconds < settings.LineTouchCooldownSeconds)
		{
			return;
		}

		lastLineTouchSeconds = courseElapsedSeconds;

		int penalty = DrivingLinePenaltyRule.SelectPenalty(currentCommands, settings.LineTouchRules,
			settings.LaneKeepingPenalty, out string matchedCommand);
		DrivingSectionAction.SplitStep(matchedCommand, out string matchedName, out _);
		bool isParkingLine = string.Equals(matchedName, DrivingScoringSettings.ParkingCommand,
			StringComparison.OrdinalIgnoreCase);
		DrivingDeductionReason reason = isParkingLine
			? DrivingDeductionReason.ParkingLineTouch
			: DrivingDeductionReason.LaneKeepingLineTouch;

		Deduct(reason, penalty, matchedCommand.Length > 0 ? matchedCommand : string.Join(" | ", currentCommands));
	}

	private void JudgeStartTime()
	{
		if (isStartSection && sectionElapsedSeconds > settings.StartTimeLimitSeconds)
		{
			Disqualify(DrivingDeductionReason.StartTimeOver, DrivingScoringSettings.StartCommand);
		}
	}

	// The parking time limit covers the whole T course, so it accumulates across its T_TEST_n steps.
	private void JudgeParkingTime(float deltaSeconds)
	{
		if (!isParkingSection)
		{
			return;
		}

		parkingElapsedSeconds += deltaSeconds;
		int expectedCount = (int)(parkingElapsedSeconds / settings.ParkingTimeLimitSeconds);
		while (parkingTimeOverCount < expectedCount)
		{
			parkingTimeOverCount++;
			Deduct(DrivingDeductionReason.ParkingTimeOver, settings.ParkingTimeOverPenalty,
				DrivingScoringSettings.ParkingCommand);
		}
	}

	// Every full interval of continuous over-speed costs one penalty; slowing down starts a new run.
	private void JudgeOverSpeed(float deltaSeconds, float speedKmh)
	{
		if (isAccelerationSection || speedKmh <= settings.SpeedLimitKmh)
		{
			ResetOverSpeed();
			return;
		}

		overSpeedElapsedSeconds += deltaSeconds;
		int expectedCount = (int)(overSpeedElapsedSeconds / settings.OverSpeedIntervalSeconds);
		while (overSpeedCount < expectedCount)
		{
			overSpeedCount++;
			Deduct(DrivingDeductionReason.OverSpeed, settings.OverSpeedPenalty, string.Join(" | ", currentCommands));
		}
	}

	private void JudgeEmergencyStop(float speedKmh)
	{
		if (!isEmergencySection || isEmergencyJudged)
		{
			return;
		}

		if (speedKmh <= settings.StoppedSpeedKmh)
		{
			isEmergencyJudged = true;
			return;
		}

		if (sectionElapsedSeconds > settings.EmergencyStopTimeLimitSeconds)
		{
			isEmergencyJudged = true;
			Deduct(DrivingDeductionReason.EmergencyStopLate, settings.EmergencyPenalty,
				DrivingScoringSettings.EmergencyCommand);
		}
	}

	// The stop zone requires a stop of at least the minimum time, no rollback, and passing it within the time limit.
	private void JudgeHillStop(float deltaSeconds, float speedKmh, float routeDistanceMeters)
	{
		if (!isHillStopSection)
		{
			return;
		}

		bool isStopped = speedKmh <= settings.StoppedSpeedKmh;
		if (!hasHillStopped)
		{
			if (isStopped)
			{
				hasHillStopped = true;
				hillStopRouteDistance = routeDistanceMeters;
			}

			return;
		}

		hillElapsedSinceStopSeconds += deltaSeconds;

		// Departure means moving forward from the stop position; rolling back is judged separately below.
		if (!isHillDepartureJudged)
		{
			if (isStopped)
			{
				hillStopSeconds += deltaSeconds;
			}
			else if (routeDistanceMeters >= hillStopRouteDistance)
			{
				isHillDepartureJudged = true;
				if (hillStopSeconds < settings.HillMinimumStopSeconds)
				{
					Deduct(DrivingDeductionReason.HillShortStop, settings.HillShortStopPenalty,
						DrivingScoringSettings.HillCommand);
				}
			}
		}

		float rollbackMeters = hillStopRouteDistance - routeDistanceMeters;
		if (rollbackMeters >= settings.HillRollbackDisqualifyMeters)
		{
			Disqualify(DrivingDeductionReason.HillRollbackOver, DrivingScoringSettings.HillCommand);
		}
		else if (rollbackMeters >= settings.HillRollbackPenaltyMeters && !isHillRollbackPenalized)
		{
			isHillRollbackPenalized = true;
			Deduct(DrivingDeductionReason.HillRollback, settings.HillRollbackPenalty, DrivingScoringSettings.HillCommand);
		}

		if (hillElapsedSinceStopSeconds > settings.HillPassTimeLimitSeconds)
		{
			Disqualify(DrivingDeductionReason.HillPassTimeOver, DrivingScoringSettings.HillCommand);
		}
	}

	// Signal-state rules (red light violation, stopping past the line) need a traffic light system and are not judged here.
	private void JudgeSignal(float deltaSeconds, float speedKmh)
	{
		if (!isSignalSection)
		{
			return;
		}

		if (speedKmh <= settings.StoppedSpeedKmh)
		{
			signalStopSeconds += deltaSeconds;
			if (signalStopSeconds >= settings.SignalStopLimitSeconds && !isSignalStopPenalized)
			{
				isSignalStopPenalized = true;
				Deduct(DrivingDeductionReason.SignalLongStop, settings.SignalStopPenalty,
					DrivingScoringSettings.SignalCommand);
			}
		}
		else
		{
			signalStopSeconds = 0f;
		}

		if (sectionElapsedSeconds > settings.SignalPassTimeLimitSeconds)
		{
			Disqualify(DrivingDeductionReason.SignalPassTimeOver, DrivingScoringSettings.SignalCommand);
		}
	}

	private void JudgeCourseTime()
	{
		float overSeconds = courseElapsedSeconds - settings.CourseTimeLimitSeconds;
		if (overSeconds <= 0f)
		{
			return;
		}

		int expectedCount = (int)(overSeconds / settings.CourseTimeOverIntervalSeconds);
		while (courseTimeOverCount < expectedCount)
		{
			courseTimeOverCount++;
			Deduct(DrivingDeductionReason.CourseTimeOver, settings.CourseTimeOverPenalty,
				string.Join(" | ", currentCommands));
		}
	}

	private void Deduct(DrivingDeductionReason reason, int points, string command)
	{
		ScoreSheet.ApplyDeduction(new DrivingDeduction(currentPointNumber, command, reason, points));
	}

	private void Disqualify(DrivingDeductionReason reason, string command)
	{
		ScoreSheet.ApplyDeduction(new DrivingDeduction(currentPointNumber, command, reason, 0, true));
	}

	private void ResetOverSpeed()
	{
		overSpeedElapsedSeconds = 0f;
		overSpeedCount = 0;
	}

	private void ClearSection()
	{
		hasSection = false;
		currentPointNumber = DrivingTestLevel.NoCurrentPoint;
		currentCommands = Array.Empty<string>();
		isStartSection = false;
		isParkingSection = false;
		isAccelerationSection = false;
		isEmergencySection = false;
		isHillStopSection = false;
		isSignalSection = false;
		sectionElapsedSeconds = 0f;
		sectionMaxSpeedKmh = 0f;
		isEmergencyJudged = false;
		hasHillStopped = false;
		hillStopRouteDistance = 0f;
		hillStopSeconds = 0f;
		hillElapsedSinceStopSeconds = 0f;
		isHillDepartureJudged = false;
		isHillRollbackPenalized = false;
		signalStopSeconds = 0f;
		isSignalStopPenalized = false;
	}

	private static bool HasCommand(IReadOnlyList<string> commands, string commandName)
	{
		foreach (string command in commands)
		{
			DrivingSectionAction.SplitStep(command, out string name, out _);
			if (string.Equals(name, commandName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasCommandStep(IReadOnlyList<string> commands, string commandName, int stepIndex)
	{
		foreach (string command in commands)
		{
			DrivingSectionAction.SplitStep(command, out string name, out int step);
			if (step == stepIndex && string.Equals(name, commandName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
