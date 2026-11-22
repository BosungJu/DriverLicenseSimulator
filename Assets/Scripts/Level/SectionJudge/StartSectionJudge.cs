// Stable identifiers shared by scoring and start instruction displays.
public enum DrivingStartInstruction
{
	None,
	SeatBelt,
	Engine,
	Headlight,
	Wiper,
	Depart,
	Gear,
	TurnSignal
}

// 출발: seat belt, engine, and two distinct randomly selected devices are instructed one at a time.
// Each must be operated within its own instruction time limit. The start line must be passed in time,
// and the left turn signal must be on when departing.
// Device operation needs vehicle controls, so the instructions only advance while a vehicle state is supplied.
public sealed class StartSectionJudge : DrivingSectionJudge
{
	private const int SelectedDeviceCount = 2;

	private float elapsedSeconds;
	private bool isDepartureJudged;
	private DrivingStartInstruction currentInstruction = DrivingStartInstruction.SeatBelt;
	private float instructionElapsedSeconds;
	private readonly DrivingStartInstruction[] deviceInstructions;
	private int deviceInstructionIndex;

	// Nothing is asked once the vehicle has departed.
	public DrivingStartInstruction CurrentInstruction =>
		isDepartureJudged ? DrivingStartInstruction.None : currentInstruction;

	public StartSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder, System.Random random = null)
		: base(settings, recorder)
	{
		random = random ?? new System.Random();
		DrivingStartInstruction[] candidates =
		{
			DrivingStartInstruction.Headlight, DrivingStartInstruction.Wiper,
			DrivingStartInstruction.Gear, DrivingStartInstruction.TurnSignal
		};
		// Partial Fisher-Yates: draw once when this start section is created, without replacement.
		for (int i = 0; i < SelectedDeviceCount; i++)
		{
			int selectedIndex = random.Next(i, candidates.Length);
			DrivingStartInstruction previous = candidates[i];
			candidates[i] = candidates[selectedIndex];
			candidates[selectedIndex] = previous;
		}
		deviceInstructions = new[] { candidates[0], candidates[1] };
	}

	public override void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
		elapsedSeconds += deltaSeconds;
		if (elapsedSeconds > Settings.StartTimeLimitSeconds)
		{
			Disqualify(DrivingDeductionReason.StartTimeOver, DrivingScoringSettings.StartCommand);
		}
	}

	public override void TickVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		if (isDepartureJudged)
		{
			return;
		}

		AdvanceDeviceInstructions(deltaSeconds, speedKmh, vehicleState);

		// Departure is the first movement in the start section.
		if (!IsDriving(speedKmh, vehicleState))
		{
			return;
		}

		isDepartureJudged = true;
		DeductUnfinishedDeviceInstructions();
		if (!vehicleState.IsLeftSignalOn)
		{
			Deduct(DrivingDeductionReason.StartSignalMissing, Settings.StartFinishSignalPenalty,
				DrivingScoringSettings.StartCommand);
		}
	}

	// At most one instruction finishes per tick, so each instruction is given before the next is judged.
	// The deadline is checked first, so a device operated after the limit is still deducted.
	// A device already on when its instruction is given passes on that tick.
	private void AdvanceDeviceInstructions(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		if (currentInstruction == DrivingStartInstruction.Depart)
		{
			return;
		}

		instructionElapsedSeconds += deltaSeconds;
		if (instructionElapsedSeconds > Settings.DeviceOperationTimeLimitSeconds)
		{
			DeductDeviceInstruction(currentInstruction);
			MoveToNextInstruction();
		}
		else if (IsInstructionOperated(currentInstruction, vehicleState)
			&& (currentInstruction != DrivingStartInstruction.Gear || IsStopped(speedKmh)))
		{
			MoveToNextInstruction();
		}
	}

	// Departing before the device instructions finish leaves those devices unoperated.
	// Departure itself needs the engine on, and driving without the seat belt is disqualified separately,
	// so only the selected optional devices are deducted here.
	private void DeductUnfinishedDeviceInstructions()
	{
		while (currentInstruction != DrivingStartInstruction.Depart)
		{
			bool isDepartureProvenInstruction = currentInstruction == DrivingStartInstruction.SeatBelt
				|| currentInstruction == DrivingStartInstruction.Engine;
			if (!isDepartureProvenInstruction)
			{
				DeductDeviceInstruction(currentInstruction);
			}

			MoveToNextInstruction();
		}
	}

	private void MoveToNextInstruction()
	{
		if (currentInstruction == DrivingStartInstruction.SeatBelt)
		{
			currentInstruction = DrivingStartInstruction.Engine;
		}
		else if (deviceInstructionIndex < deviceInstructions.Length)
		{
			currentInstruction = deviceInstructions[deviceInstructionIndex++];
		}
		else
		{
			currentInstruction = DrivingStartInstruction.Depart;
		}

		instructionElapsedSeconds = 0f;
	}

	private void DeductDeviceInstruction(DrivingStartInstruction instruction)
	{
		Deduct(GetNotOperatedReason(instruction), Settings.DeviceOperationPenalty, DrivingScoringSettings.StartCommand);
	}

	private static DrivingDeductionReason GetNotOperatedReason(DrivingStartInstruction instruction)
	{
		switch (instruction)
		{
			case DrivingStartInstruction.SeatBelt:
				return DrivingDeductionReason.SeatBeltNotFastened;
			case DrivingStartInstruction.Engine:
				return DrivingDeductionReason.EngineNotStarted;
			case DrivingStartInstruction.Headlight:
				return DrivingDeductionReason.HeadlightNotOperated;
			case DrivingStartInstruction.Gear:
				return DrivingDeductionReason.GearNotOperated;
			case DrivingStartInstruction.TurnSignal:
				return DrivingDeductionReason.StartTurnSignalNotOperated;
			default:
				return DrivingDeductionReason.WiperNotOperated;
		}
	}

	private static bool IsInstructionOperated(DrivingStartInstruction instruction, DrivingVehicleState vehicleState)
	{
		switch (instruction)
		{
			case DrivingStartInstruction.SeatBelt:
				return vehicleState.IsSeatBeltOn;
			case DrivingStartInstruction.Engine:
				return vehicleState.IsEngineOn;
			case DrivingStartInstruction.Headlight:
				return vehicleState.IsHeadlightOn && !vehicleState.IsHighBeamOn;
			case DrivingStartInstruction.Gear:
				return vehicleState.Gear == VehicleGear.Neutral && vehicleState.IsParkingBrakeOn;
			case DrivingStartInstruction.TurnSignal:
				return vehicleState.IsLeftSignalOn && !vehicleState.IsRightSignalOn && !vehicleState.IsHazardOn;
			case DrivingStartInstruction.Wiper:
				return vehicleState.IsWiperOn;
			default:
				return false;
		}
	}
}
