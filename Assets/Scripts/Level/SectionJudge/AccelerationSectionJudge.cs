using System;

// 속도가속 구간: the target speed must be reached somewhere in the section.
// The course speed limit is excluded here by the evaluator, since it is a course-wide rule.
public sealed class AccelerationSectionJudge : DrivingSectionJudge
{
	private float maxSpeedKmh;

	public AccelerationSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder)
		: base(settings, recorder)
	{
	}

	public override void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
		maxSpeedKmh = Math.Max(maxSpeedKmh, speedKmh);
	}

	public override void End()
	{
		if (maxSpeedKmh < Settings.AccelerationTargetKmh)
		{
			Deduct(DrivingDeductionReason.AccelerationTooSlow, Settings.AccelerationPenalty,
				DrivingScoringSettings.AccelerationCommand);
		}
	}
}
