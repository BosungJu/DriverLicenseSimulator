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
	// Low or high beam.
	public bool IsHeadlightOn { get; }
	public bool IsWiperOn { get; }
	public bool IsTurnSignalOn => IsLeftSignalOn || IsRightSignalOn;

	public DrivingVehicleState(bool isSeatBeltOn, bool isEngineOn, bool isHazardOn, bool isLeftSignalOn,
		bool isRightSignalOn, bool isParkingBrakeOn, bool isHeadlightOn = false, bool isWiperOn = false)
	{
		IsSeatBeltOn = isSeatBeltOn;
		IsEngineOn = isEngineOn;
		IsHazardOn = isHazardOn;
		IsLeftSignalOn = isLeftSignalOn;
		IsRightSignalOn = isRightSignalOn;
		IsParkingBrakeOn = isParkingBrakeOn;
		IsHeadlightOn = isHeadlightOn;
		IsWiperOn = isWiperOn;
	}
}

// Applies the course test scoring table to section, speed, line-contact, and vehicle control input.
// Section-specific rules live in DrivingSectionJudge subclasses created per section;
// this class keeps the course-wide rules (course time, speed limit, line touch, signal violation, seat belt, engine).
// Time only advances through Tick, so the rules are deterministic and testable without a scene.
// Rules that need vehicle controls are skipped while no DrivingVehicleState is supplied.
public sealed class DrivingTestScoreEvaluator
{
	private readonly DrivingScoringSettings settings;
	private readonly DrivingDeductionRecorder recorder;
	// Judges of the current section, in the order their rules are applied.
	private readonly List<DrivingSectionJudge> sectionJudges = new List<DrivingSectionJudge>();
	// Shared by the T_TEST_n sections of one parking course; null outside it.
	private ParkingSectionJudge parkingCourseJudge;
	// Judge of the current start section, kept so its instruction can be shown; null outside it.
	private StartSectionJudge startJudge;

	private bool isCourseRunning;
	private bool hasSection;
	private IReadOnlyList<string> currentCommands = Array.Empty<string>();
	// Section traits that change course-wide rules.
	private bool isAccelerationSection;
	private bool isSignalSection;
	private bool isRightTurnSection;

	private float courseElapsedSeconds;
	private float overSpeedElapsedSeconds;
	private float lastLineTouchSeconds;
	private int courseTimeOverCount;
	private int overSpeedCount;

	// Vehicle control state. Section flags only count once a vehicle state was supplied in that section;
	// the finish judge reads them as the approach section's record.
	private bool hasSectionVehicleState;
	private bool hasSectionTurnSignal;
	private bool wasEngineOn;

	public DrivingScoreSheet ScoreSheet { get; }
	public bool IsCourseRunning => isCourseRunning;
	public float CourseElapsedSeconds => courseElapsedSeconds;
	public DrivingStartInstruction CurrentStartInstruction =>
		startJudge != null ? startJudge.CurrentInstruction : DrivingStartInstruction.None;

	public DrivingTestScoreEvaluator(DrivingScoringSettings settings)
	{
		this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
		ScoreSheet = new DrivingScoreSheet(settings.MaxScore, settings.PassingScore);
		recorder = new DrivingDeductionRecorder(ScoreSheet);
		Reset();
	}

	public void Reset()
	{
		ScoreSheet.Reset();
		isCourseRunning = false;
		ClearSection();
		parkingCourseJudge = null;
		courseElapsedSeconds = 0f;
		overSpeedElapsedSeconds = 0f;
		lastLineTouchSeconds = float.NegativeInfinity;
		courseTimeOverCount = 0;
		overSpeedCount = 0;
		wasEngineOn = false;
	}

	// The course starts with the first section, so time before the start point is not scored.
	// crossingSpeedKmh is the vehicle speed when it crossed into this point, or null when it could not be sampled;
	// it is the exit speed of the ending section and the entry speed of the new one.
	public void BeginSection(int pointNumber, IReadOnlyList<string> commands, float? crossingSpeedKmh = null)
	{
		IReadOnlyList<string> nextCommands = commands ?? Array.Empty<string>();

		// The course completes as soon as the finish point is reached, so the finish signal is judged
		// on the approach section that is ending now.
		bool hasApproachVehicleState = hasSection && hasSectionVehicleState;
		bool hasApproachTurnSignal = hasSectionTurnSignal;

		if (hasSection)
		{
			bool continuesParkingCourse = HasActiveParkingCommand(nextCommands);
			EndSection(continuesParkingCourse, crossingSpeedKmh);
		}

		isCourseRunning = true;
		hasSection = true;
		recorder.AssignPointNumber(pointNumber);
		currentCommands = nextCommands;
		isAccelerationSection = HasCommand(currentCommands, DrivingScoringSettings.AccelerationCommand)
			&& !HasCommandStep(currentCommands, DrivingScoringSettings.AccelerationCommand, DrivingScoringSettings.AccelerationExitStep);
		isSignalSection = HasCommand(currentCommands, DrivingScoringSettings.SignalCommand);
		isRightTurnSection = HasCommand(currentCommands, DrivingScoringSettings.RightTurnCommand);
		AddSectionJudges(hasApproachVehicleState, hasApproachTurnSignal);

		// The speed limit excludes the acceleration section, so an over-speed run does not carry into or out of it.
		if (isAccelerationSection)
		{
			ResetOverSpeed();
		}

		// Indexed loops, because a deduction listener may reset the evaluator and clear the judges.
		for (int i = 0; i < sectionJudges.Count; i++)
		{
			sectionJudges[i].Begin();
		}

		if (crossingSpeedKmh.HasValue)
		{
			for (int i = 0; i < sectionJudges.Count; i++)
			{
				sectionJudges[i].ObserveEntrySpeed(crossingSpeedKmh.Value);
			}
		}
	}

	public void EndSection()
	{
		EndSection(false, null);
	}

	public void CompleteCourse()
	{
		EndSection();
		isCourseRunning = false;
	}

	// routeDistanceMeters is the vehicle position along the current section's route direction (larger = further ahead).
	// vehicleState is null when the vehicle controls cannot be read; their rules are then skipped.
	// approachSignalState is the light of the stop line the vehicle is approaching, or null when none governs it.
	public void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters = 0f,
		DrivingVehicleState? vehicleState = null, TrafficLightState? approachSignalState = null)
	{
		if (!isCourseRunning || !hasSection || deltaSeconds <= 0f)
		{
			return;
		}

		courseElapsedSeconds += deltaSeconds;

		// Indexed loops, because a deduction listener may reset the evaluator and clear the judges.
		for (int i = 0; i < sectionJudges.Count; i++)
		{
			sectionJudges[i].Tick(deltaSeconds, speedKmh, routeDistanceMeters, approachSignalState);
		}

		JudgeOverSpeed(deltaSeconds, speedKmh);
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

		recorder.Deduct(reason, penalty, matchedCommand.Length > 0 ? matchedCommand : string.Join(" | ", currentCommands));
	}

	// Called when the vehicle front reaches a stop line; doing so on a stop light is a signal violation.
	// A right turn may proceed on any light, so it is never a signal violation.
	public void RegisterStopLineCrossing(TrafficLightState signalState)
	{
		if (!isCourseRunning || !hasSection || isRightTurnSection)
		{
			return;
		}

		bool isStopSignal = signalState == TrafficLightState.Red
			|| (signalState == TrafficLightState.Yellow && settings.IsYellowSignalViolation);
		if (!isStopSignal)
		{
			return;
		}

		recorder.Disqualify(DrivingDeductionReason.SignalViolation,
			isSignalSection ? DrivingScoringSettings.SignalCommand : string.Join(" | ", currentCommands));
	}

	// Judges run their section-end rules before the section state is cleared, so deductions belong to the ending section.
	// A parking course that continues into the next T_TEST_n section keeps its judge running instead.
	private void EndSection(bool continuesParkingCourse, float? exitSpeedKmh)
	{
		if (!hasSection)
		{
			return;
		}

		for (int i = 0; i < sectionJudges.Count; i++)
		{
			DrivingSectionJudge sectionJudge = sectionJudges[i];
			bool isContinuingParkingJudge = continuesParkingCourse && sectionJudge == parkingCourseJudge;
			if (isContinuingParkingJudge)
			{
				continue;
			}

			if (exitSpeedKmh.HasValue)
			{
				sectionJudge.ObserveExitSpeed(exitSpeedKmh.Value);
			}

			sectionJudge.End();
		}

		if (!continuesParkingCourse)
		{
			parkingCourseJudge = null;
		}

		ClearSection();
	}

	// The order matches the order the section rules are applied in each tick and at section end.
	private void AddSectionJudges(bool hasApproachVehicleState, bool hasApproachTurnSignal)
	{
		if (HasCommand(currentCommands, DrivingScoringSettings.StartCommand))
		{
			startJudge = new StartSectionJudge(settings, recorder);
			sectionJudges.Add(startJudge);
		}

		if (isAccelerationSection)
		{
			sectionJudges.Add(new AccelerationSectionJudge(settings, recorder));
		}

		if (HasCommand(currentCommands, DrivingScoringSettings.EmergencyCommand))
		{
			sectionJudges.Add(new EmergencySectionJudge(settings, recorder));
		}

		if (HasCommandStep(currentCommands, DrivingScoringSettings.HillCommand, settings.HillStopStep))
		{
			sectionJudges.Add(new HillStopSectionJudge(settings, recorder));
		}

		if (isSignalSection)
		{
			sectionJudges.Add(new SignalSectionJudge(settings, recorder, isRightTurnSection));
		}

		if (HasCommand(currentCommands, DrivingScoringSettings.LeftTurnCommand))
		{
			sectionJudges.Add(new TurnSectionJudge(settings, recorder, true));
		}

		if (isRightTurnSection)
		{
			sectionJudges.Add(new TurnSectionJudge(settings, recorder, false));
		}

		if (HasActiveParkingCommand(currentCommands))
		{
			if (parkingCourseJudge == null)
			{
				parkingCourseJudge = new ParkingSectionJudge(settings, recorder);
			}

			sectionJudges.Add(parkingCourseJudge);
		}

		if (HasCommand(currentCommands, DrivingScoringSettings.FinishCommand))
		{
			sectionJudges.Add(new FinishSectionJudge(settings, recorder, hasApproachVehicleState, hasApproachTurnSignal));
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
			recorder.Deduct(DrivingDeductionReason.OverSpeed, settings.OverSpeedPenalty, string.Join(" | ", currentCommands));
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
			recorder.Deduct(DrivingDeductionReason.CourseTimeOver, settings.CourseTimeOverPenalty,
				string.Join(" | ", currentCommands));
		}
	}

	// Seat belt and engine rules apply to the whole course; section judges read the controls afterwards.
	private void JudgeVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		hasSectionVehicleState = true;
		hasSectionTurnSignal |= vehicleState.IsTurnSignalOn;

		// Movement with the engine off (e.g. settling after the spawn drop) is not the driver departing.
		bool isDriving = vehicleState.IsEngineOn && speedKmh > settings.StoppedSpeedKmh;

		JudgeSeatBelt(isDriving, vehicleState);
		JudgeEngineStall(vehicleState);

		for (int i = 0; i < sectionJudges.Count; i++)
		{
			sectionJudges[i].TickVehicleControls(deltaSeconds, speedKmh, vehicleState);
		}
	}

	// The belt must be worn for the whole test. It is judged while driving,
	// because the course already starts while the driver is still getting ready at the start point.
	private void JudgeSeatBelt(bool isDriving, DrivingVehicleState vehicleState)
	{
		if (isDriving && !vehicleState.IsSeatBeltOn)
		{
			recorder.Disqualify(DrivingDeductionReason.SeatBeltNotWorn, string.Join(" | ", currentCommands));
		}
	}

	// Every on-to-off engine change during the course counts as failing to keep the engine running.
	private void JudgeEngineStall(DrivingVehicleState vehicleState)
	{
		bool isStalled = wasEngineOn && !vehicleState.IsEngineOn;
		wasEngineOn = vehicleState.IsEngineOn;

		if (isStalled)
		{
			recorder.Deduct(DrivingDeductionReason.EngineStalled, settings.EngineStallPenalty, string.Join(" | ", currentCommands));
		}
	}

	private void ResetOverSpeed()
	{
		overSpeedElapsedSeconds = 0f;
		overSpeedCount = 0;
	}

	private void ClearSection()
	{
		hasSection = false;
		recorder.AssignPointNumber(DrivingTestLevel.NoCurrentPoint);
		currentCommands = Array.Empty<string>();
		isAccelerationSection = false;
		isSignalSection = false;
		isRightTurnSection = false;
		sectionJudges.Clear();
		startJudge = null;
		hasSectionVehicleState = false;
		hasSectionTurnSignal = false;
	}

	// The terminal marker closes judging at entry; other commands on that point remain active.
	private static bool HasActiveParkingCommand(IReadOnlyList<string> commands)
	{
		return HasCommand(commands, DrivingScoringSettings.ParkingCommand)
			&& !HasCommandStep(commands, DrivingScoringSettings.ParkingCommand, DrivingScoringSettings.ParkingExitStep);
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
