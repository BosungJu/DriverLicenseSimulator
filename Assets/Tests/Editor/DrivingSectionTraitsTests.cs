using NUnit.Framework;

public class DrivingSectionTraitsTests
{
	private const int HillStopStep = 1;

	#region Normal Cases

	[TestCase("start")]
	[TestCase("START")]
	public void FromCommands_StartCommand_SetsOnlyStart(string command)
	{
		DrivingSectionTraits traits = DrivingSectionTraits.FromCommands(new[] { command }, HillStopStep);

		Assert.IsTrue(traits.IsStart);
		Assert.IsFalse(traits.IsAcceleration || traits.IsEmergency || traits.IsHillStop || traits.IsSignal ||
			traits.IsLeftTurn || traits.IsRightTurn || traits.IsParking || traits.IsFinish);
	}

	[Test]
	public void FromCommands_TurnWithEmergency_SetsBothTraits()
	{
		DrivingSectionTraits traits = DrivingSectionTraits.FromCommands(new[] { "left", "on_emergency" }, HillStopStep);

		Assert.IsTrue(traits.IsLeftTurn);
		Assert.IsTrue(traits.IsEmergency);
		Assert.IsFalse(traits.IsRightTurn);
	}

	[Test]
	public void FromCommands_SignalWithRightTurnAndFinish_SetsEachTrait()
	{
		DrivingSectionTraits signalTraits = DrivingSectionTraits.FromCommands(new[] { "signal", "right" }, HillStopStep);
		DrivingSectionTraits finishTraits = DrivingSectionTraits.FromCommands(new[] { "end" }, HillStopStep);

		Assert.IsTrue(signalTraits.IsSignal);
		Assert.IsTrue(signalTraits.IsRightTurn);
		Assert.IsTrue(finishTraits.IsFinish);
	}

	#endregion

	#region Step Boundaries

	[TestCase("fast", true)]
	[TestCase("fast_0", true)]
	[TestCase("fast_1", false)]
	public void FromCommands_AccelerationStep_ExcludesExitStep(string command, bool expectedAcceleration)
	{
		DrivingSectionTraits traits = DrivingSectionTraits.FromCommands(new[] { command }, HillStopStep);

		Assert.AreEqual(expectedAcceleration, traits.IsAcceleration);
	}

	[TestCase("hill_0", 1, false)]
	[TestCase("hill_1", 1, true)]
	[TestCase("hill_2", 2, true)]
	[TestCase("hill", 1, false)]
	public void FromCommands_HillStep_OnlyConfiguredStopStepIsHillStop(string command, int hillStopStep, bool expectedHillStop)
	{
		DrivingSectionTraits traits = DrivingSectionTraits.FromCommands(new[] { command }, hillStopStep);

		Assert.AreEqual(expectedHillStop, traits.IsHillStop);
	}

	[TestCase("T_TEST_1", true)]
	[TestCase("T_TEST_2", true)]
	[TestCase("T_TEST_3", false)]
	public void FromCommands_ParkingStep_ExcludesTerminalStep(string command, bool expectedParking)
	{
		DrivingSectionTraits traits = DrivingSectionTraits.FromCommands(new[] { command }, HillStopStep);

		Assert.AreEqual(expectedParking, traits.IsParking);
	}

	#endregion

	#region Empty Input

	[Test]
	public void FromCommands_NullCommands_SetsNoTrait()
	{
		DrivingSectionTraits traits = DrivingSectionTraits.FromCommands(null, HillStopStep);

		Assert.IsFalse(traits.IsStart || traits.IsAcceleration || traits.IsEmergency || traits.IsHillStop ||
			traits.IsSignal || traits.IsLeftTurn || traits.IsRightTurn || traits.IsParking || traits.IsFinish);
	}

	#endregion
}
