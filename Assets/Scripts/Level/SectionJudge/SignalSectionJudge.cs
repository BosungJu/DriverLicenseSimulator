// 신호교차로: stopping too long in the intersection is deducted.
// The signal violation itself is judged by the evaluator when a stop line is crossed,
// because a stop line can govern any section.
public sealed class SignalSectionJudge : DrivingSectionJudge
{
	private readonly bool isRightTurn;

	private float stopSeconds;
	private bool isStopPenalized;

	public SignalSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder, bool isRightTurn)
		: base(settings, recorder)
	{
		this.isRightTurn = isRightTurn;
	}

	// Waiting before the stop line for a red or yellow light is required, so it does not count as a long stop.
	// A right turn does not wait for the light, so its stops always count.
	public override void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
		bool isWaitingForGreen = !isRightTurn
			&& approachSignalState.HasValue
			&& approachSignalState.Value != TrafficLightState.Green;
		if (!IsStopped(speedKmh) || isWaitingForGreen)
		{
			stopSeconds = 0f;
			return;
		}

		stopSeconds += deltaSeconds;
		if (stopSeconds >= Settings.SignalStopLimitSeconds && !isStopPenalized)
		{
			isStopPenalized = true;
			Deduct(DrivingDeductionReason.SignalLongStop, Settings.SignalStopPenalty,
				DrivingScoringSettings.SignalCommand);
		}
	}
}
