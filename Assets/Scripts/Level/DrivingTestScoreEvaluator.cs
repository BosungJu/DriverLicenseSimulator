using System;
using System.Collections.Generic;

// Driver controls checked by the scoring table, sampled once per scoring tick.
public readonly struct DrivingVehicleState
{
	public bool IsSeatBeltOn { get; }
	public bool IsEngineOn { get; }
	public bool IsHazardOn { get; }
	public bool IsLeftSignalOn { get; }
	public bool IsRightSignalOn { get; }
	public bool IsParkingBrakeOn { get; }
	public bool IsTurnSignalOn => IsLeftSignalOn || IsRightSignalOn;

	public DrivingVehicleState(bool isSeatBeltOn, bool isEngineOn, bool isHazardOn, bool isLeftSignalOn,
		bool isRightSignalOn, bool isParkingBrakeOn)
	{
		IsSeatBeltOn = isSeatBeltOn;
		IsEngineOn = isEngineOn;
		IsHazardOn = isHazardOn;
		IsLeftSignalOn = isLeftSignalOn;
		IsRightSignalOn = isRightSignalOn;
		IsParkingBrakeOn = isParkingBrakeOn;
	}
}

// Applies the course test scoring table to section, speed, line-contact, and vehicle control input.
// Time only advances through Tick, so the rules are deterministic and testable without a scene.
// Rules that need vehicle controls are skipped while no DrivingVehicleState is supplied.
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
	private bool isLeftTurnSection;
	private bool isRightTurnSection;

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

	// Emergency stop follow-up: hazard lights on after stopping, off again when departing.
	private bool hasEmergencyStopped;
	private float emergencyStoppedSeconds;
	private bool isEmergencyHazardJudged;
	private bool isEmergencyDepartureJudged;
	private bool isEmergencyHazardEarlyJudged;

	// Hill stop zone state; route distance is measured along the section's route direction.
	// hasHillStopped marks the first stop (rollback and pass time count from it);
	// only a continuous stop of the minimum duration fulfils the stop requirement.
	private bool hasHillStopped;
	private bool isHillStopHolding;
	private float hillStopHoldSeconds;
	private bool hasHillHeldMinimumStop;
	private float hillStopRouteDistance;
	private float hillElapsedSinceStopSeconds;
	private bool isHillRollbackPenalized;

	private float signalStopSeconds;
	private bool isSignalStopPenalized;

	// Vehicle control state. Section flags only count once a vehicle state was supplied in that section.
	private bool hasSectionVehicleState;
	private bool hasSectionLeftSignal;
	private bool hasSectionRightSignal;
	private bool isStartDepartureJudged;
	private bool wasEngineOn;
	// The parking course spans several T_TEST_n sections, so its parking brake check spans them too.
	private bool hasParkingVehicleState;
	private bool hasParkingBrakeApplied;

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
		wasEngineOn = false;
		hasParkingVehicleState = false;
		hasParkingBrakeApplied = false;
	}

	// The course starts with the first section, so time before the start point is not scored.
	public void BeginSection(int pointNumber, IReadOnlyList<string> commands)
	{
		IReadOnlyList<string> nextCommands = commands ?? Array.Empty<string>();

		// The course completes as soon as the finish point is reached, so the finish signal is judged
		// on the approach section that is ending now.
		bool hasApproachVehicleState = hasSection && hasSectionVehicleState;
		bool hasApproachTurnSignal = hasSectionLeftSignal || hasSectionRightSignal;

		if (hasSection)
		{
			bool continuesParkingCourse = HasCommand(nextCommands, DrivingScoringSettings.ParkingCommand);
			EndSection(continuesParkingCourse);
		}

		isCourseRunning = true;
		hasSection = true;
		currentPointNumber = pointNumber;
		currentCommands = nextCommands;
		isStartSection = HasCommand(currentCommands, DrivingScoringSettings.StartCommand);
		isParkingSection = HasCommand(currentCommands, DrivingScoringSettings.ParkingCommand);
		isAccelerationSection = HasCommand(currentCommands, DrivingScoringSettings.AccelerationCommand);
		isEmergencySection = HasCommand(currentCommands, DrivingScoringSettings.EmergencyCommand);
		isHillStopSection = HasCommandStep(currentCommands, DrivingScoringSettings.HillCommand, settings.HillStopStep);
		isSignalSection = HasCommand(currentCommands, DrivingScoringSettings.SignalCommand);
		isLeftTurnSection = HasCommand(currentCommands, DrivingScoringSettings.LeftTurnCommand);
		isRightTurnSection = HasCommand(currentCommands, DrivingScoringSettings.RightTurnCommand);
		sectionElapsedSeconds = 0f;
		sectionMaxSpeedKmh = 0f;
		isEmergencyJudged = false;

		// The speed limit excludes the acceleration section, so an over-speed run does not carry into or out of it.
		if (isAccelerationSection)
		{
			ResetOverSpeed();
		}

		if (HasCommand(currentCommands, DrivingScoringSettings.FinishCommand))
		{
			JudgeFinishSignal(hasApproachVehicleState, hasApproachTurnSignal);
		}
	}

	public void EndSection()
	{
		EndSection(false);
	}

	public void CompleteCourse()
	{
		EndSection();
		isCourseRunning = false;
	}

	// routeDistanceMeters is the vehicle position along the current section's route direction (larger = further ahead).
	// vehicleState is null when the vehicle controls cannot be read; their rules are then skipped.
	public void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters = 0f,
		DrivingVehicleState? vehicleState = null)
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

		if (vehicleState.HasValue)
		{
			JudgeVehicleControls(deltaSeconds, speedKmh, vehicleState.Value);
		}
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

	// Section-end rules run before the section state is cleared, so deductions belong to the ending section.
	private void EndSection(bool continuesParkingCourse)
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

		if (isHillStopSection && !hasHillHeldMinimumStop)
		{
			Disqualify(DrivingDeductionReason.HillNotStopped, DrivingScoringSettings.HillCommand);
		}

		JudgeTurnSignal();

		if (isParkingSection && !continuesParkingCourse)
		{
			JudgeParkingBrake();
		}

		ClearSection();
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

	// The stop zone requires a stop of the minimum duration, no rollback, and passing it within the time limit after stopping.
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
				isHillStopHolding = true;
				hillStopRouteDistance = routeDistanceMeters;
			}

			return;
		}

		hillElapsedSinceStopSeconds += deltaSeconds;

		// Short stops do not add up; moving again restarts the required stop.
		// Like the first stop, a new stop counts from its first stopped tick.
		if (isStopped)
		{
			if (isHillStopHolding)
			{
				hillStopHoldSeconds += deltaSeconds;
				hasHillHeldMinimumStop |= hillStopHoldSeconds >= settings.HillMinimumStopSeconds;
			}

			isHillStopHolding = true;
		}
		else
		{
			isHillStopHolding = false;
			hillStopHoldSeconds = 0f;
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

	// Seat belt, engine, turn signal, hazard light and parking brake rules read the driver's controls.
	private void JudgeVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		RecordControlState(speedKmh, vehicleState);

		// Movement with the engine off (e.g. settling after the spawn drop) is not the driver departing.
		bool isDriving = vehicleState.IsEngineOn && speedKmh > settings.StoppedSpeedKmh;

		JudgeSeatBelt(isDriving, vehicleState);
		JudgeEngineStall(vehicleState);
		JudgeStartSignal(isDriving, vehicleState);
		JudgeEmergencyHazard(deltaSeconds, speedKmh, vehicleState);
	}

	private void RecordControlState(float speedKmh, DrivingVehicleState vehicleState)
	{
		hasSectionVehicleState = true;
		hasSectionLeftSignal |= vehicleState.IsLeftSignalOn;
		hasSectionRightSignal |= vehicleState.IsRightSignalOn;

		// The brake only counts as parking when applied while stopped, not toggled while driving through.
		if (isParkingSection)
		{
			hasParkingVehicleState = true;
			hasParkingBrakeApplied |= vehicleState.IsParkingBrakeOn && speedKmh <= settings.StoppedSpeedKmh;
		}
	}

	// The belt must be worn for the whole test. It is judged while driving,
	// because the course already starts while the driver is still getting ready at the start point.
	private void JudgeSeatBelt(bool isDriving, DrivingVehicleState vehicleState)
	{
		if (isDriving && !vehicleState.IsSeatBeltOn)
		{
			Disqualify(DrivingDeductionReason.SeatBeltNotWorn, string.Join(" | ", currentCommands));
		}
	}

	// Every on-to-off engine change during the course counts as failing to keep the engine running.
	private void JudgeEngineStall(DrivingVehicleState vehicleState)
	{
		bool isStalled = wasEngineOn && !vehicleState.IsEngineOn;
		wasEngineOn = vehicleState.IsEngineOn;

		if (isStalled)
		{
			Deduct(DrivingDeductionReason.EngineStalled, settings.EngineStallPenalty, string.Join(" | ", currentCommands));
		}
	}

	// Departure is the first movement in the start section; a turn signal must be on at that moment.
	private void JudgeStartSignal(bool isDriving, DrivingVehicleState vehicleState)
	{
		if (!isStartSection || isStartDepartureJudged || !isDriving)
		{
			return;
		}

		isStartDepartureJudged = true;
		if (!vehicleState.IsTurnSignalOn)
		{
			Deduct(DrivingDeductionReason.StartSignalMissing, settings.StartFinishSignalPenalty,
				DrivingScoringSettings.StartCommand);
		}
	}

	// The hazard lights belong after the emergency stop: not while still moving, on in time once stopped,
	// and off again when the vehicle departs.
	private void JudgeEmergencyHazard(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		if (!isEmergencySection)
		{
			return;
		}

		bool isStopped = speedKmh <= settings.StoppedSpeedKmh;
		float secondsSinceStop = deltaSeconds;
		if (!hasEmergencyStopped)
		{
			if (!isStopped)
			{
				JudgeEmergencyHazardEarly(vehicleState);
				return;
			}

			// Time counts from the stop, so hazard lights already on at the stopping tick are in time.
			hasEmergencyStopped = true;
			secondsSinceStop = 0f;
		}

		// The deadline is checked before accepting the hazard lights, so turning them on after it still counts as late.
		if (!isEmergencyHazardJudged)
		{
			emergencyStoppedSeconds += secondsSinceStop;
			bool isPastDeadline = emergencyStoppedSeconds > settings.EmergencyHazardTimeLimitSeconds;
			bool isHazardLate = isPastDeadline || (!isStopped && !vehicleState.IsHazardOn);
			if (isHazardLate)
			{
				isEmergencyHazardJudged = true;
				Deduct(DrivingDeductionReason.EmergencyHazardLate, settings.EmergencyPenalty,
					DrivingScoringSettings.EmergencyCommand);
			}
			else if (vehicleState.IsHazardOn)
			{
				isEmergencyHazardJudged = true;
			}
		}

		if (!isEmergencyDepartureJudged && !isStopped)
		{
			isEmergencyDepartureJudged = true;
			if (vehicleState.IsHazardOn)
			{
				Deduct(DrivingDeductionReason.EmergencyHazardNotOff, settings.EmergencyPenalty,
					DrivingScoringSettings.EmergencyCommand);
			}
		}
	}

	// Test machines deduct hazard lights switched on before the stop is complete.
	private void JudgeEmergencyHazardEarly(DrivingVehicleState vehicleState)
	{
		if (isEmergencyHazardEarlyJudged || !vehicleState.IsHazardOn)
		{
			return;
		}

		isEmergencyHazardEarlyJudged = true;
		Deduct(DrivingDeductionReason.EmergencyHazardEarly, settings.EmergencyPenalty,
			DrivingScoringSettings.EmergencyCommand);
	}

	// A left/right section needs its matching turn signal at some point while driving through it.
	private void JudgeTurnSignal()
	{
		if (!hasSectionVehicleState)
		{
			return;
		}

		if (isLeftTurnSection && !hasSectionLeftSignal)
		{
			Deduct(DrivingDeductionReason.TurnSignalMissing, settings.TurnSignalPenalty,
				DrivingScoringSettings.LeftTurnCommand);
		}

		if (isRightTurnSection && !hasSectionRightSignal)
		{
			Deduct(DrivingDeductionReason.TurnSignalMissing, settings.TurnSignalPenalty,
				DrivingScoringSettings.RightTurnCommand);
		}
	}

	private void JudgeParkingBrake()
	{
		bool isParkingBrakeMissing = hasParkingVehicleState && !hasParkingBrakeApplied;
		hasParkingVehicleState = false;
		hasParkingBrakeApplied = false;

		if (isParkingBrakeMissing)
		{
			Deduct(DrivingDeductionReason.ParkingBrakeNotApplied, settings.ParkingBrakePenalty,
				DrivingScoringSettings.ParkingCommand);
		}
	}

	// Arriving at the finish needs a turn signal at some point while driving the approach section.
	private void JudgeFinishSignal(bool hasApproachVehicleState, bool hasApproachTurnSignal)
	{
		if (hasApproachVehicleState && !hasApproachTurnSignal)
		{
			Deduct(DrivingDeductionReason.FinishSignalMissing, settings.StartFinishSignalPenalty,
				DrivingScoringSettings.FinishCommand);
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
		isLeftTurnSection = false;
		isRightTurnSection = false;
		sectionElapsedSeconds = 0f;
		sectionMaxSpeedKmh = 0f;
		isEmergencyJudged = false;
		hasEmergencyStopped = false;
		emergencyStoppedSeconds = 0f;
		isEmergencyHazardJudged = false;
		isEmergencyDepartureJudged = false;
		isEmergencyHazardEarlyJudged = false;
		hasHillStopped = false;
		isHillStopHolding = false;
		hillStopHoldSeconds = 0f;
		hasHillHeldMinimumStop = false;
		hillStopRouteDistance = 0f;
		hillElapsedSinceStopSeconds = 0f;
		isHillRollbackPenalized = false;
		signalStopSeconds = 0f;
		isSignalStopPenalized = false;
		hasSectionVehicleState = false;
		hasSectionLeftSignal = false;
		hasSectionRightSignal = false;
		isStartDepartureJudged = false;
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
