using System;

// A speed-based four-speed transmission. Ratios are simulation tuning, not manufacturer specifications.
public sealed class AutomaticTransmission
{
	private const int FirstGear = 1;
	private const int TopGear = 4;
	private const float PercentageScale = 100f;
	private const int FirstUpshiftSpeedPercent = 20;
	private const int SecondUpshiftSpeedPercent = 40;
	private const int ThirdUpshiftSpeedPercent = 65;
	private const float DownshiftSpeedRatio = 0.75f;
	private const float SecondGearTorqueRatio = 0.75f;
	private const float ThirdGearTorqueRatio = 0.55f;
	private const float FourthGearTorqueRatio = 0.4f;
	private const float ShiftingTorqueRatio = 0.25f;

	private float shiftRemainingSeconds;

	public int CurrentGear { get; private set; } = FirstGear;
	public bool IsShifting => shiftRemainingSeconds > 0f;
	public float TorqueRatio => GetGearTorqueRatio() * (IsShifting ? ShiftingTorqueRatio : 1f);

	public void Reset()
	{
		CurrentGear = FirstGear;
		shiftRemainingSeconds = 0f;
	}

	// speedKmh is signed in the forward driving direction; rolling backwards returns to first gear.
	public void Tick(float speedKmh, float maxSpeedKmh, float shiftDurationSeconds, float deltaSeconds)
	{
		if (maxSpeedKmh <= 0f || speedKmh <= 0f)
		{
			Reset();
			return;
		}

		if (deltaSeconds <= 0f)
		{
			return;
		}

		if (IsShifting)
		{
			shiftRemainingSeconds = Math.Max(0f, shiftRemainingSeconds - deltaSeconds);
			return;
		}

		bool shouldUpshift = CurrentGear < TopGear
			&& speedKmh >= GetUpshiftSpeedKmh(CurrentGear, maxSpeedKmh);
		bool shouldDownshift = CurrentGear > FirstGear
			&& speedKmh <= GetUpshiftSpeedKmh(CurrentGear - 1, maxSpeedKmh) * DownshiftSpeedRatio;
		if (!shouldUpshift && !shouldDownshift)
		{
			return;
		}

		CurrentGear += shouldUpshift ? 1 : -1;
		shiftRemainingSeconds = Math.Max(0f, shiftDurationSeconds);
	}

	private static float GetUpshiftSpeedKmh(int gear, float maxSpeedKmh)
	{
		int speedPercent;
		switch (gear)
		{
			case 1: speedPercent = FirstUpshiftSpeedPercent; break;
			case 2: speedPercent = SecondUpshiftSpeedPercent; break;
			default: speedPercent = ThirdUpshiftSpeedPercent; break;
		}

		return maxSpeedKmh * speedPercent / PercentageScale;
	}

	private float GetGearTorqueRatio()
	{
		switch (CurrentGear)
		{
			case 2: return SecondGearTorqueRatio;
			case 3: return ThirdGearTorqueRatio;
			case 4: return FourthGearTorqueRatio;
			default: return 1f;
		}
	}
}
