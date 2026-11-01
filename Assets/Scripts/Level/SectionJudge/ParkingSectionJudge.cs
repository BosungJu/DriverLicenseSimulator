// 직각주차: the time limit and the parking brake check cover the whole T course.
// The course spans several T_TEST_n sections, so the evaluator keeps one judge alive across them
// and calls End only when the course is left.
public sealed class ParkingSectionJudge : DrivingSectionJudge
{
	private float elapsedSeconds;
	private int timeOverCount;
	private bool hasVehicleState;
	private bool hasParkingBrakeApplied;

	public ParkingSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder)
		: base(settings, recorder)
	{
	}

	public override void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
		elapsedSeconds += deltaSeconds;
		int expectedCount = (int)(elapsedSeconds / Settings.ParkingTimeLimitSeconds);
		while (timeOverCount < expectedCount)
		{
			timeOverCount++;
			Deduct(DrivingDeductionReason.ParkingTimeOver, Settings.ParkingTimeOverPenalty,
				DrivingScoringSettings.ParkingCommand);
		}
	}

	// The brake only counts as parking when applied while stopped, not toggled while driving through.
	public override void TickVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		hasVehicleState = true;
		hasParkingBrakeApplied |= vehicleState.IsParkingBrakeOn && IsStopped(speedKmh);
	}

	public override void End()
	{
		if (hasVehicleState && !hasParkingBrakeApplied)
		{
			Deduct(DrivingDeductionReason.ParkingBrakeNotApplied, Settings.ParkingBrakePenalty,
				DrivingScoringSettings.ParkingCommand);
		}
	}
}
