// 종료: arriving at the finish needs a turn signal at some point while driving the approach section.
// The course completes as soon as the finish point is reached, so the signal is judged on entry
// from what the evaluator recorded during the approach section.
public sealed class FinishSectionJudge : DrivingSectionJudge
{
	private readonly bool hasApproachVehicleState;
	private readonly bool hasApproachTurnSignal;

	public FinishSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder,
		bool hasApproachVehicleState, bool hasApproachTurnSignal)
		: base(settings, recorder)
	{
		this.hasApproachVehicleState = hasApproachVehicleState;
		this.hasApproachTurnSignal = hasApproachTurnSignal;
	}

	public override void Begin()
	{
		if (hasApproachVehicleState && !hasApproachTurnSignal)
		{
			Deduct(DrivingDeductionReason.FinishSignalMissing, Settings.StartFinishSignalPenalty,
				DrivingScoringSettings.FinishCommand);
		}
	}
}
