// 경사로 정지·출발: the stop zone requires a stop of the minimum duration, no rollback,
// and passing it within the time limit after stopping.
// Route distance is measured along the section's route direction.
public sealed class HillStopSectionJudge : DrivingSectionJudge
{
	// hasStopped marks the first stop (rollback and pass time count from it);
	// only a continuous stop of the minimum duration fulfils the stop requirement.
	private bool hasStopped;
	private bool isStopHolding;
	private float stopHoldSeconds;
	private bool hasHeldMinimumStop;
	private float stopRouteDistance;
	private float elapsedSinceStopSeconds;
	private bool isRollbackPenalized;

	public HillStopSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder)
		: base(settings, recorder)
	{
	}

	public override void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
		bool isStopped = IsStopped(speedKmh);
		if (!hasStopped)
		{
			if (isStopped)
			{
				hasStopped = true;
				isStopHolding = true;
				stopRouteDistance = routeDistanceMeters;
			}

			return;
		}

		elapsedSinceStopSeconds += deltaSeconds;

		// Short stops do not add up; moving again restarts the required stop.
		// Like the first stop, a new stop counts from its first stopped tick.
		if (isStopped)
		{
			if (isStopHolding)
			{
				stopHoldSeconds += deltaSeconds;
				hasHeldMinimumStop |= stopHoldSeconds >= Settings.HillMinimumStopSeconds;
			}

			isStopHolding = true;
		}
		else
		{
			isStopHolding = false;
			stopHoldSeconds = 0f;
		}

		float rollbackMeters = stopRouteDistance - routeDistanceMeters;
		if (rollbackMeters >= Settings.HillRollbackDisqualifyMeters)
		{
			Disqualify(DrivingDeductionReason.HillRollbackOver, DrivingScoringSettings.HillCommand);
		}
		else if (rollbackMeters >= Settings.HillRollbackPenaltyMeters && !isRollbackPenalized)
		{
			isRollbackPenalized = true;
			Deduct(DrivingDeductionReason.HillRollback, Settings.HillRollbackPenalty, DrivingScoringSettings.HillCommand);
		}

		if (elapsedSinceStopSeconds > Settings.HillPassTimeLimitSeconds)
		{
			Disqualify(DrivingDeductionReason.HillPassTimeOver, DrivingScoringSettings.HillCommand);
		}
	}

	public override void End()
	{
		if (!hasHeldMinimumStop)
		{
			Disqualify(DrivingDeductionReason.HillNotStopped, DrivingScoringSettings.HillCommand);
		}
	}
}
