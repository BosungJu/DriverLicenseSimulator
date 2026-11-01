// 돌발상황 급정지: stop in time, then hazard lights on after stopping and off again when departing.
public sealed class EmergencySectionJudge : DrivingSectionJudge
{
	private float elapsedSeconds;
	private bool isStopJudged;

	private bool hasStopped;
	private float stoppedSeconds;
	private bool isHazardJudged;
	private bool isDepartureJudged;
	private bool isHazardEarlyJudged;

	public EmergencySectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder)
		: base(settings, recorder)
	{
	}

	public override void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
		elapsedSeconds += deltaSeconds;
		if (isStopJudged)
		{
			return;
		}

		if (IsStopped(speedKmh))
		{
			isStopJudged = true;
			return;
		}

		if (elapsedSeconds > Settings.EmergencyStopTimeLimitSeconds)
		{
			isStopJudged = true;
			Deduct(DrivingDeductionReason.EmergencyStopLate, Settings.EmergencyPenalty,
				DrivingScoringSettings.EmergencyCommand);
		}
	}

	// The hazard lights belong after the emergency stop: not while still moving, on in time once stopped,
	// and off again when the vehicle departs.
	public override void TickVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		bool isStopped = IsStopped(speedKmh);
		float secondsSinceStop = deltaSeconds;
		if (!hasStopped)
		{
			if (!isStopped)
			{
				JudgeHazardEarly(vehicleState);
				return;
			}

			// Time counts from the stop, so hazard lights already on at the stopping tick are in time.
			hasStopped = true;
			secondsSinceStop = 0f;
		}

		// The deadline is checked before accepting the hazard lights, so turning them on after it still counts as late.
		if (!isHazardJudged)
		{
			stoppedSeconds += secondsSinceStop;
			bool isPastDeadline = stoppedSeconds > Settings.EmergencyHazardTimeLimitSeconds;
			bool isHazardLate = isPastDeadline || (!isStopped && !vehicleState.IsHazardOn);
			if (isHazardLate)
			{
				isHazardJudged = true;
				Deduct(DrivingDeductionReason.EmergencyHazardLate, Settings.EmergencyPenalty,
					DrivingScoringSettings.EmergencyCommand);
			}
			else if (vehicleState.IsHazardOn)
			{
				isHazardJudged = true;
			}
		}

		if (!isDepartureJudged && !isStopped)
		{
			isDepartureJudged = true;
			if (vehicleState.IsHazardOn)
			{
				Deduct(DrivingDeductionReason.EmergencyHazardNotOff, Settings.EmergencyPenalty,
					DrivingScoringSettings.EmergencyCommand);
			}
		}
	}

	// Test machines deduct hazard lights switched on before the stop is complete.
	private void JudgeHazardEarly(DrivingVehicleState vehicleState)
	{
		if (isHazardEarlyJudged || !vehicleState.IsHazardOn)
		{
			return;
		}

		isHazardEarlyJudged = true;
		Deduct(DrivingDeductionReason.EmergencyHazardEarly, Settings.EmergencyPenalty,
			DrivingScoringSettings.EmergencyCommand);
	}
}
