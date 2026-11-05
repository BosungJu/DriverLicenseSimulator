// What the start section currently asks the driver to do, in instruction order.
public enum DrivingStartInstruction
{
	None,
	SeatBelt,
	Engine,
	Headlight,
	Wiper,
	Depart
}

// 출발: before departing, the seat belt, the engine, the headlight, and then the wiper are instructed one at a
// time and each must be operated within its own instruction time limit. The start line must be passed in time,
// and the left turn signal must be on when departing.
// Device operation needs vehicle controls, so the instructions only advance while a vehicle state is supplied.
public sealed class StartSectionJudge : DrivingSectionJudge
{
	private float elapsedSeconds;
	private bool isDepartureJudged;
	private DrivingStartInstruction currentInstruction = DrivingStartInstruction.SeatBelt;
	private float instructionElapsedSeconds;

	// Nothing is asked once the vehicle has departed.
	public DrivingStartInstruction CurrentInstruction =>
		isDepartureJudged ? DrivingStartInstruction.None : currentInstruction;

	public StartSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder)
		: base(settings, recorder)
	{
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

		AdvanceDeviceInstructions(deltaSeconds, vehicleState);

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
	private void AdvanceDeviceInstructions(float deltaSeconds, DrivingVehicleState vehicleState)
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
		else if (IsInstructionOperated(currentInstruction, vehicleState))
		{
			MoveToNextInstruction();
		}
	}

	// Departing before the device instructions finish leaves those devices unoperated.
	// Departure itself needs the engine on, and driving without the seat belt is disqualified separately,
	// so only the headlight and the wiper are deducted here.
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
		currentInstruction = GetNextInstruction(currentInstruction);
		instructionElapsedSeconds = 0f;
	}

	private void DeductDeviceInstruction(DrivingStartInstruction instruction)
	{
		Deduct(GetNotOperatedReason(instruction), Settings.DeviceOperationPenalty, DrivingScoringSettings.StartCommand);
	}

	private static DrivingStartInstruction GetNextInstruction(DrivingStartInstruction instruction)
	{
		switch (instruction)
		{
			case DrivingStartInstruction.SeatBelt:
				return DrivingStartInstruction.Engine;
			case DrivingStartInstruction.Engine:
				return DrivingStartInstruction.Headlight;
			case DrivingStartInstruction.Headlight:
				return DrivingStartInstruction.Wiper;
			default:
				return DrivingStartInstruction.Depart;
		}
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
				return vehicleState.IsHeadlightOn;
			case DrivingStartInstruction.Wiper:
				return vehicleState.IsWiperOn;
			default:
				return false;
		}
	}
}
