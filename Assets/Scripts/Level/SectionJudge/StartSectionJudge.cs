// 출발: the start line must be passed in time, and a turn signal must be on when departing.
public sealed class StartSectionJudge : DrivingSectionJudge
{
	private float elapsedSeconds;
	private bool isDepartureJudged;

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

	// Departure is the first movement in the start section; a turn signal must be on at that moment.
	public override void TickVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		if (isDepartureJudged || !IsDriving(speedKmh, vehicleState))
		{
			return;
		}

		isDepartureJudged = true;
		if (!vehicleState.IsTurnSignalOn)
		{
			Deduct(DrivingDeductionReason.StartSignalMissing, Settings.StartFinishSignalPenalty,
				DrivingScoringSettings.StartCommand);
		}
	}
}
