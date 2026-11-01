// 좌회전 또는 우회전: the matching turn signal must be on at some point while driving through the section.
public sealed class TurnSectionJudge : DrivingSectionJudge
{
	private readonly bool isLeftTurn;

	private bool hasVehicleState;
	private bool hasMatchingSignal;

	public TurnSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder, bool isLeftTurn)
		: base(settings, recorder)
	{
		this.isLeftTurn = isLeftTurn;
	}

	public override void TickVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		hasVehicleState = true;
		hasMatchingSignal |= isLeftTurn ? vehicleState.IsLeftSignalOn : vehicleState.IsRightSignalOn;
	}

	// Skipped when the controls were never read in this section.
	public override void End()
	{
		if (!hasVehicleState || hasMatchingSignal)
		{
			return;
		}

		string turnCommand = isLeftTurn ? DrivingScoringSettings.LeftTurnCommand : DrivingScoringSettings.RightTurnCommand;
		Deduct(DrivingDeductionReason.TurnSignalMissing, Settings.TurnSignalPenalty, turnCommand);
	}
}
