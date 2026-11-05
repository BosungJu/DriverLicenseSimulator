using NUnit.Framework;

public class AutomaticTransmissionTests
{
	private const float ShiftDurationSeconds = 0.35f;
	private const float PhysicsStepSeconds = 0.02f;

	#region Shifting

	[Test]
	public void Constructor_NewTransmission_StartsInFirstGearWithFullTorque()
	{
		var transmission = new AutomaticTransmission();

		Assert.That(transmission.CurrentGear, Is.EqualTo(1));
		Assert.That(transmission.IsShifting, Is.False);
		Assert.That(transmission.TorqueRatio, Is.EqualTo(1f));
	}

	[TestCase(100f, 20f, 40f, 65f)]
	[TestCase(110f, 22f, 44f, 71.5f)]
	public void Tick_Accelerating_UpshiftsAtScaledSpeedBoundaries(float maxSpeedKmh,
		float secondGearSpeedKmh, float thirdGearSpeedKmh, float fourthGearSpeedKmh)
	{
		var transmission = new AutomaticTransmission();

		transmission.Tick(secondGearSpeedKmh - 0.01f, maxSpeedKmh, ShiftDurationSeconds, PhysicsStepSeconds);
		Assert.That(transmission.CurrentGear, Is.EqualTo(1));
		transmission.Tick(secondGearSpeedKmh, maxSpeedKmh, ShiftDurationSeconds, PhysicsStepSeconds);
		Assert.That(transmission.CurrentGear, Is.EqualTo(2));

		transmission.Tick(secondGearSpeedKmh, maxSpeedKmh, ShiftDurationSeconds, ShiftDurationSeconds);
		transmission.Tick(thirdGearSpeedKmh, maxSpeedKmh, ShiftDurationSeconds, PhysicsStepSeconds);
		Assert.That(transmission.CurrentGear, Is.EqualTo(3));

		transmission.Tick(thirdGearSpeedKmh, maxSpeedKmh, ShiftDurationSeconds, ShiftDurationSeconds);
		transmission.Tick(fourthGearSpeedKmh, maxSpeedKmh, ShiftDurationSeconds, PhysicsStepSeconds);
		Assert.That(transmission.CurrentGear, Is.EqualTo(4));
	}

	[Test]
	public void Tick_WhileShifting_ReducesTorqueAndDefersAnotherShift()
	{
		var transmission = new AutomaticTransmission();
		transmission.Tick(90f, 100f, ShiftDurationSeconds, PhysicsStepSeconds);

		transmission.Tick(90f, 100f, ShiftDurationSeconds, 0.1f);

		Assert.That(transmission.CurrentGear, Is.EqualTo(2));
		Assert.That(transmission.IsShifting, Is.True);
		Assert.That(transmission.TorqueRatio, Is.EqualTo(0.1875f).Within(0.0001f));

		transmission.Tick(90f, 100f, ShiftDurationSeconds, ShiftDurationSeconds);
		Assert.That(transmission.CurrentGear, Is.EqualTo(2));
		Assert.That(transmission.IsShifting, Is.False);
		Assert.That(transmission.TorqueRatio, Is.EqualTo(0.75f).Within(0.0001f));
	}

	[TestCase(15.01f)]
	[TestCase(19.99f)]
	[TestCase(20f)]
	public void Tick_SpeedNearUpshiftBoundary_KeepsSecondGear(float speedKmh)
	{
		AutomaticTransmission transmission = CreateTransmissionInGear(2);

		transmission.Tick(speedKmh, 100f, ShiftDurationSeconds, PhysicsStepSeconds);

		Assert.That(transmission.CurrentGear, Is.EqualTo(2));
		Assert.That(transmission.IsShifting, Is.False);
	}

	[TestCase(2, 15f, 1)]
	[TestCase(3, 30f, 2)]
	[TestCase(4, 48.75f, 3)]
	public void Tick_AtDownshiftBoundary_ShiftsDownOneGear(int initialGear, float speedKmh, int expectedGear)
	{
		AutomaticTransmission transmission = CreateTransmissionInGear(initialGear);

		transmission.Tick(speedKmh, 100f, ShiftDurationSeconds, PhysicsStepSeconds);

		Assert.That(transmission.CurrentGear, Is.EqualTo(expectedGear));
		Assert.That(transmission.IsShifting, Is.True);
	}

	[Test]
	public void Tick_AboveTopGearSpeed_RemainsInFourthGear()
	{
		AutomaticTransmission transmission = CreateTransmissionInGear(4);

		transmission.Tick(150f, 100f, ShiftDurationSeconds, PhysicsStepSeconds);

		Assert.That(transmission.CurrentGear, Is.EqualTo(4));
		Assert.That(transmission.IsShifting, Is.False);
		Assert.That(transmission.TorqueRatio, Is.EqualTo(0.4f).Within(0.0001f));
	}

	#endregion

	#region Reset And Boundaries

	[TestCase(0f)]
	[TestCase(-2f)]
	public void Tick_StoppedOrRollingBack_ReturnsToFirstGear(float speedKmh)
	{
		AutomaticTransmission transmission = CreateTransmissionInGear(4);

		transmission.Tick(speedKmh, 100f, ShiftDurationSeconds, PhysicsStepSeconds);

		Assert.That(transmission.CurrentGear, Is.EqualTo(1));
		Assert.That(transmission.IsShifting, Is.False);
	}

	[TestCase(0f)]
	[TestCase(-10f)]
	public void Tick_NonPositiveSpeedLimit_ResetsTransmission(float maxSpeedKmh)
	{
		AutomaticTransmission transmission = CreateTransmissionInGear(3);

		transmission.Tick(50f, maxSpeedKmh, ShiftDurationSeconds, PhysicsStepSeconds);

		Assert.That(transmission.CurrentGear, Is.EqualTo(1));
		Assert.That(transmission.IsShifting, Is.False);
	}

	[TestCase(0f)]
	[TestCase(-1f)]
	public void Tick_NoElapsedTime_DoesNotShift(float deltaSeconds)
	{
		var transmission = new AutomaticTransmission();

		transmission.Tick(50f, 100f, ShiftDurationSeconds, deltaSeconds);

		Assert.That(transmission.CurrentGear, Is.EqualTo(1));
	}

	[Test]
	public void Tick_ZeroShiftDuration_CompletesShiftImmediately()
	{
		var transmission = new AutomaticTransmission();

		transmission.Tick(20f, 100f, 0f, PhysicsStepSeconds);

		Assert.That(transmission.CurrentGear, Is.EqualTo(2));
		Assert.That(transmission.IsShifting, Is.False);
		Assert.That(transmission.TorqueRatio, Is.EqualTo(0.75f).Within(0.0001f));
	}

	[Test]
	public void Reset_DuringShift_RestoresFirstGearAndClearsPendingShift()
	{
		var transmission = new AutomaticTransmission();
		transmission.Tick(20f, 100f, ShiftDurationSeconds, PhysicsStepSeconds);

		transmission.Reset();
		transmission.Reset();

		Assert.That(transmission.CurrentGear, Is.EqualTo(1));
		Assert.That(transmission.IsShifting, Is.False);
		Assert.That(transmission.TorqueRatio, Is.EqualTo(1f));
	}

	#endregion

	private static AutomaticTransmission CreateTransmissionInGear(int targetGear)
	{
		var transmission = new AutomaticTransmission();
		for (int gear = 1; gear < targetGear; gear++)
		{
			transmission.Tick(90f, 100f, ShiftDurationSeconds, PhysicsStepSeconds);
			transmission.Tick(90f, 100f, ShiftDurationSeconds, ShiftDurationSeconds);
		}

		Assert.That(transmission.CurrentGear, Is.EqualTo(targetGear));
		return transmission;
	}
}
