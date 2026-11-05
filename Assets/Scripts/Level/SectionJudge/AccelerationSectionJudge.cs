using System;

// 속도가속 구간: the target speed must be reached somewhere in the section.
// Entering or leaving the section at the boundary speed limit is deducted, so the acceleration
// and the slowing down must both happen inside it.
// The course speed limit is excluded here by the evaluator, since it is a course-wide rule.
public sealed class AccelerationSectionJudge : DrivingSectionJudge
{
	private float maxSpeedKmh;
	private bool isEntrySpeedJudged;
	private bool hasTicked;
	private float lastTickSpeedKmh;
	private float? crossingExitSpeedKmh;

	public AccelerationSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder)
		: base(settings, recorder)
	{
	}

	public override void ObserveEntrySpeed(float speedKmh)
	{
		JudgeEntrySpeedOnce(speedKmh);
	}

	public override void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
		// Without a crossing sample, the first tick after the section begins is the entry speed.
		JudgeEntrySpeedOnce(speedKmh);

		maxSpeedKmh = Math.Max(maxSpeedKmh, speedKmh);
		hasTicked = true;
		lastTickSpeedKmh = speedKmh;
	}

	public override void ObserveExitSpeed(float speedKmh)
	{
		crossingExitSpeedKmh = speedKmh;
	}

	public override void End()
	{
		if (maxSpeedKmh < Settings.AccelerationTargetKmh)
		{
			Deduct(DrivingDeductionReason.AccelerationTooSlow, Settings.AccelerationPenalty,
				DrivingScoringSettings.AccelerationCommand);
		}

		// Prefer the crossing sample; otherwise the last tick before the section ends is the exit speed.
		if (crossingExitSpeedKmh.HasValue)
		{
			JudgeExitSpeed(crossingExitSpeedKmh.Value);
		}
		else if (hasTicked)
		{
			JudgeExitSpeed(lastTickSpeedKmh);
		}
	}

	private void JudgeEntrySpeedOnce(float entrySpeedKmh)
	{
		if (isEntrySpeedJudged)
		{
			return;
		}

		isEntrySpeedJudged = true;
		if (entrySpeedKmh >= Settings.AccelerationEntrySpeedLimitKmh)
		{
			Deduct(DrivingDeductionReason.AccelerationEntryTooFast, Settings.AccelerationEntrySpeedPenalty,
				DrivingScoringSettings.AccelerationCommand);
		}
	}

	private void JudgeExitSpeed(float exitSpeedKmh)
	{
		if (exitSpeedKmh >= Settings.AccelerationExitSpeedLimitKmh)
		{
			Deduct(DrivingDeductionReason.AccelerationExitTooFast, Settings.AccelerationExitSpeedPenalty,
				DrivingScoringSettings.AccelerationCommand);
		}
	}
}
