using System.Collections.Generic;
using NUnit.Framework;

// Expected values come from the class 1/2 ordinary course test scoring table.
public class DrivingScoringTests
{
	private const int LaneKeepingPenalty = 15;
	private const int ParkingLinePenalty = 10;
	private const float StoppedKmh = 0f;

	private DrivingTestScoreEvaluator evaluator;

	#region Setup

	[SetUp]
	public void SetUp()
	{
		evaluator = new DrivingTestScoreEvaluator(new DrivingScoringSettings());
	}

	private DrivingScoreSheet ScoreSheet => evaluator.ScoreSheet;

	private void Begin(int pointNumber, params string[] commands)
	{
		evaluator.BeginSection(pointNumber, commands);
	}

	#endregion

	#region Line Touch

	[TestCase("hill_0", LaneKeepingPenalty, DrivingDeductionReason.LaneKeepingLineTouch)]
	[TestCase("left", LaneKeepingPenalty, DrivingDeductionReason.LaneKeepingLineTouch)]
	[TestCase("right", LaneKeepingPenalty, DrivingDeductionReason.LaneKeepingLineTouch)]
	[TestCase("T_TEST_0", ParkingLinePenalty, DrivingDeductionReason.ParkingLineTouch)]
	[TestCase("T_TEST_3", ParkingLinePenalty, DrivingDeductionReason.ParkingLineTouch)]
	public void RegisterLineTouch_Section_UsesSectionPenalty(string command, int expectedPenalty,
		DrivingDeductionReason expectedReason)
	{
		Begin(7, command);

		evaluator.RegisterLineTouch();

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(expectedPenalty));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(expectedReason));
		Assert.That(ScoreSheet.GetSectionDeduction(7), Is.EqualTo(expectedPenalty));
	}

	[Test]
	public void RegisterLineTouch_WithinCooldown_CountsOnce()
	{
		Begin(1, "hill_0");

		evaluator.RegisterLineTouch();
		evaluator.Tick(0.5f, StoppedKmh);
		evaluator.RegisterLineTouch();
		evaluator.Tick(0.6f, StoppedKmh);
		evaluator.RegisterLineTouch();

		Assert.That(ScoreSheet.Deductions.Count, Is.EqualTo(2));
		Assert.That(ScoreSheet.Score, Is.EqualTo(70));
		Assert.That(ScoreSheet.IsPassed, Is.False);
	}

	[Test]
	public void RegisterLineTouch_BeforeCourseOrAfterCompletion_IsIgnored()
	{
		evaluator.RegisterLineTouch();
		Begin(22, "end");
		evaluator.CompleteCourse();
		evaluator.RegisterLineTouch();

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	#endregion

	#region Section Rules

	[Test]
	public void Tick_StartNotPassedWithin30Seconds_Disqualifies()
	{
		Begin(0, "start");

		evaluator.Tick(30f, StoppedKmh);
		Assert.That(ScoreSheet.IsDisqualified, Is.False);

		evaluator.Tick(0.1f, StoppedKmh);

		Assert.That(ScoreSheet.IsDisqualified, Is.True);
		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.StartTimeOver));
		Assert.That(ScoreSheet.Score, Is.EqualTo(100));
		Assert.That(ScoreSheet.IsPassed, Is.False);
	}

	[Test]
	public void Tick_ParkingOverTwoMinutesAcrossSteps_DeductsEveryTwoMinutes()
	{
		Begin(11, "T_TEST_0");
		evaluator.Tick(60f, StoppedKmh);
		Begin(12, "T_TEST_1");
		evaluator.Tick(59f, StoppedKmh);
		Assert.That(ScoreSheet.Deductions, Is.Empty);

		evaluator.Tick(2f, StoppedKmh);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));

		evaluator.Tick(120f, StoppedKmh);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(20));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.ParkingTimeOver));
	}

	[TestCase(19.9f, 10)]
	[TestCase(20f, 0)]
	public void EndSection_AccelerationSection_DeductsWhenBelow20Kmh(float maxSpeedKmh, int expectedDeduction)
	{
		Begin(8, "fast");
		evaluator.Tick(1f, maxSpeedKmh);

		evaluator.EndSection();

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(expectedDeduction));
	}

	[Test]
	public void Tick_EmergencyNotStoppedWithin2Seconds_DeductsOnce()
	{
		Begin(5, "left", "on_emergency");

		evaluator.Tick(2f, 15f);
		Assert.That(ScoreSheet.Deductions, Is.Empty);

		evaluator.Tick(0.1f, 15f);
		evaluator.Tick(1f, 15f);

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.EmergencyStopLate));
	}

	[Test]
	public void Tick_EmergencyStoppedWithin2Seconds_NoDeduction()
	{
		Begin(5, "on_emergency");

		evaluator.Tick(1.5f, StoppedKmh);
		evaluator.Tick(3f, 15f);

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	#endregion

	#region Hill and Signal

	private const float HillStopDistance = 5f;

	private void StopOnHill(float stopSeconds)
	{
		Begin(2, "hill_1");
		evaluator.Tick(0.5f, 10f, HillStopDistance - 1f);
		evaluator.Tick(0.1f, StoppedKmh, HillStopDistance);
		evaluator.Tick(stopSeconds, StoppedKmh, HillStopDistance);
	}

	// Test machines give the departure cue after a 3 s stop; the stop time counts from the first stopped tick.
	[TestCase(3f)]
	[TestCase(10f)]
	public void Tick_HillStoppedAtLeast3SecondsThenDeparts_NoDeduction(float stopSeconds)
	{
		StopOnHill(stopSeconds);

		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.1f);
		evaluator.EndSection();

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[TestCase(0f)]
	[TestCase(2.9f)]
	public void EndSection_HillDepartsBefore3Seconds_DisqualifiesAsNotStopped(float stopSeconds)
	{
		StopOnHill(stopSeconds);

		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.1f);
		Assert.That(ScoreSheet.IsDisqualified, Is.False);

		evaluator.EndSection();

		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.HillNotStopped));
	}

	[Test]
	public void EndSection_HillShortStopsDoNotAddUp_DisqualifiesAsNotStopped()
	{
		StopOnHill(2f);
		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.1f);
		evaluator.Tick(0.1f, StoppedKmh, HillStopDistance + 0.1f);
		evaluator.Tick(2f, StoppedKmh, HillStopDistance + 0.1f);
		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.2f);

		evaluator.EndSection();

		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.HillNotStopped));
	}

	[Test]
	public void EndSection_HillRestopFirstTickOnly_DisqualifiesAsNotStopped()
	{
		StopOnHill(1f);
		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.1f);
		evaluator.Tick(3f, StoppedKmh, HillStopDistance + 0.1f);
		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.2f);

		evaluator.EndSection();

		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.HillNotStopped));
	}

	[Test]
	public void EndSection_HillShortStopThenFullStop_NoDeduction()
	{
		StopOnHill(1f);
		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.1f);
		evaluator.Tick(0.1f, StoppedKmh, HillStopDistance + 0.1f);
		evaluator.Tick(3f, StoppedKmh, HillStopDistance + 0.1f);
		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.2f);

		evaluator.EndSection();

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[Test]
	public void Tick_HillRollsBack_Deducts10AtHalfMeterAndDisqualifiesAtOneMeter()
	{
		StopOnHill(3f);

		evaluator.Tick(0.5f, 3f, HillStopDistance - 0.6f);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.HillRollback));
		Assert.That(ScoreSheet.IsDisqualified, Is.False);

		evaluator.Tick(0.5f, 3f, HillStopDistance - 1f);
		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.HillRollbackOver));
	}

	[Test]
	public void Tick_HillNotPassedWithin30SecondsAfterStop_Disqualifies()
	{
		StopOnHill(30f);
		Assert.That(ScoreSheet.IsDisqualified, Is.False);

		evaluator.Tick(0.1f, StoppedKmh, HillStopDistance);

		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.HillPassTimeOver));
	}

	[Test]
	public void EndSection_HillStopZoneWithoutStopping_Disqualifies()
	{
		Begin(2, "hill_1");
		evaluator.Tick(5f, 10f, 3f);

		Begin(3, "hill_2");

		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.HillNotStopped));
		Assert.That(ScoreSheet.Disqualification.PointNumber, Is.EqualTo(2));
	}

	[TestCase("hill_0")]
	[TestCase("hill_2")]
	public void EndSection_OtherHillStepWithoutStopping_NoDeduction(string command)
	{
		Begin(1, command);
		evaluator.Tick(5f, 10f, 3f);

		evaluator.EndSection();

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[Test]
	public void Tick_SignalStoppedTwentySeconds_Deducts5Once()
	{
		Begin(9, "right", "signal");

		evaluator.Tick(19f, StoppedKmh);
		Assert.That(ScoreSheet.Deductions, Is.Empty);

		evaluator.Tick(1f, StoppedKmh);
		evaluator.Tick(5f, StoppedKmh);

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(5));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.SignalLongStop));
	}

	// The official table has no pass time limit at the signal intersection; only stopping 20 s or more is deducted.
	[Test]
	public void Tick_SignalDrivingLongerThan30Seconds_NoDisqualification()
	{
		Begin(9, "right", "signal");

		evaluator.Tick(40f, 10f);

		Assert.That(ScoreSheet.IsDisqualified, Is.False);
		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	#endregion

	#region Course Rules

	[Test]
	public void Tick_OverSpeedOutsideAccelerationSection_DeductsEveryThreeSeconds()
	{
		Begin(3, "left");

		evaluator.Tick(2.9f, 25f);
		Assert.That(ScoreSheet.Deductions, Is.Empty);

		evaluator.Tick(0.2f, 25f);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(3));

		evaluator.Tick(1f, 15f);
		evaluator.Tick(2f, 25f);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(3));

		evaluator.Tick(1.1f, 25f);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(6));
	}

	[Test]
	public void Tick_OverSpeedInAccelerationSection_NoSpeedLimitDeduction()
	{
		Begin(8, "fast");

		evaluator.Tick(10f, 40f);

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[Test]
	public void Tick_CourseOverDesignatedTime_DeductsEveryFiveSeconds()
	{
		Begin(3, "left");

		evaluator.Tick(568f + 4.9f, StoppedKmh);
		Assert.That(ScoreSheet.Deductions, Is.Empty);

		evaluator.Tick(0.2f, StoppedKmh);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(3));

		evaluator.Tick(10f, StoppedKmh);
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(9));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.CourseTimeOver));
	}

	[Test]
	public void Reset_AfterDeductions_RestartsScoringFromFullScore()
	{
		Begin(0, "start");
		evaluator.Tick(31f, 25f);
		evaluator.RegisterLineTouch();

		evaluator.Reset();
		Begin(0, "start");
		evaluator.Tick(1f, StoppedKmh);

		Assert.That(ScoreSheet.Score, Is.EqualTo(100));
		Assert.That(ScoreSheet.IsDisqualified, Is.False);
		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	#endregion

	#region Vehicle Controls

	private const float DrivingKmh = 10f;

	private static DrivingVehicleState Controls(bool seatBelt = true, bool engine = true, bool hazard = false,
		bool leftSignal = false, bool rightSignal = false, bool parkingBrake = false)
	{
		return new DrivingVehicleState(seatBelt, engine, hazard, leftSignal, rightSignal, parkingBrake);
	}

	private void Tick(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
		evaluator.Tick(deltaSeconds, speedKmh, 0f, vehicleState);
	}

	[Test]
	public void Tick_DrivingWithoutSeatBelt_Disqualifies()
	{
		Begin(3, "left");

		Tick(1f, StoppedKmh, Controls(seatBelt: false));
		Assert.That(ScoreSheet.IsDisqualified, Is.False);

		Tick(1f, DrivingKmh, Controls(seatBelt: false));

		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.SeatBeltNotWorn));
	}

	[Test]
	public void Tick_MovingWithEngineOffAndNoSeatBelt_NoDisqualification()
	{
		Begin(0, "start");

		Tick(0.5f, DrivingKmh, Controls(seatBelt: false, engine: false));

		Assert.That(ScoreSheet.IsDisqualified, Is.False);
		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[Test]
	public void Tick_EngineTurnsOffDuringCourse_Deducts5EachTime()
	{
		Begin(3, "left");

		Tick(1f, DrivingKmh, Controls(engine: true));
		Tick(1f, StoppedKmh, Controls(engine: false));
		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(5));

		Tick(1f, StoppedKmh, Controls(engine: true));
		Tick(1f, StoppedKmh, Controls(engine: false));

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.EngineStalled));
	}

	[TestCase(true, 0)]
	[TestCase(false, 5)]
	public void Tick_StartDeparture_DeductsWithoutTurnSignalAtDeparture(bool isSignalOnAtDeparture, int expectedDeduction)
	{
		Begin(0, "start");

		Tick(1f, StoppedKmh, Controls());
		Tick(1f, DrivingKmh, Controls(leftSignal: isSignalOnAtDeparture));
		Tick(1f, DrivingKmh, Controls());

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(expectedDeduction));
	}

	[Test]
	public void Tick_StartSignalCancelledBeforeDeparture_Deducts5()
	{
		Begin(0, "start");

		Tick(1f, StoppedKmh, Controls(leftSignal: true));
		Tick(1f, StoppedKmh, Controls());
		Tick(1f, DrivingKmh, Controls());

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(5));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.StartSignalMissing));
	}

	[TestCase("left", true, false, 0)]
	[TestCase("left", false, true, 5)]
	[TestCase("right", false, true, 0)]
	[TestCase("right", false, false, 5)]
	public void EndSection_TurnSection_DeductsWithoutMatchingSignal(string command, bool leftSignal, bool rightSignal,
		int expectedDeduction)
	{
		Begin(4, command);
		Tick(1f, DrivingKmh, Controls(leftSignal: leftSignal, rightSignal: rightSignal));
		Tick(1f, DrivingKmh, Controls());

		evaluator.EndSection();

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(expectedDeduction));
	}

	[Test]
	public void EndSection_TurnSectionWithoutVehicleState_NoDeduction()
	{
		Begin(4, "left");
		evaluator.Tick(1f, DrivingKmh);

		evaluator.EndSection();

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[TestCase(true, 0)]
	[TestCase(false, 10)]
	public void BeginSection_LeavingParkingCourse_DeductsWithoutParkingBrake(bool isBrakeApplied, int expectedDeduction)
	{
		Begin(11, "T_TEST_0");
		Tick(1f, StoppedKmh, Controls());
		Begin(12, "T_TEST_1");
		Tick(1f, StoppedKmh, Controls(parkingBrake: isBrakeApplied));
		Assert.That(ScoreSheet.Deductions, Is.Empty);

		Begin(13, "fast");

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(expectedDeduction));
		if (expectedDeduction > 0)
		{
			Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.ParkingBrakeNotApplied));
			Assert.That(ScoreSheet.Deductions[0].PointNumber, Is.EqualTo(12));
		}
	}

	[Test]
	public void BeginSection_ParkingBrakeOnlyWhileMoving_Deducts10()
	{
		Begin(11, "T_TEST_0");
		Tick(1f, DrivingKmh, Controls(parkingBrake: true));
		Tick(1f, StoppedKmh, Controls());

		Begin(12, "fast");

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.ParkingBrakeNotApplied));
	}

	private void StopForEmergency()
	{
		Begin(5, "on_emergency");
		Tick(1f, StoppedKmh, Controls());
	}

	[Test]
	public void Tick_EmergencyHazardOnThenOffBeforeDeparture_NoDeduction()
	{
		StopForEmergency();

		Tick(2f, StoppedKmh, Controls(hazard: true));
		Tick(1f, StoppedKmh, Controls());
		Tick(1f, DrivingKmh, Controls());

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[Test]
	public void Tick_EmergencyHazardNotOnWithin3Seconds_Deducts10Once()
	{
		StopForEmergency();

		Tick(3f, StoppedKmh, Controls());
		Assert.That(ScoreSheet.Deductions, Is.Empty);

		Tick(0.1f, StoppedKmh, Controls());
		Tick(1f, DrivingKmh, Controls());

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.EmergencyHazardLate));
	}

	[Test]
	public void Tick_EmergencyHazardOnAtStopThenOffBeforeDeparture_NoDeduction()
	{
		Begin(5, "on_emergency");

		Tick(1f, StoppedKmh, Controls(hazard: true));
		Tick(1f, DrivingKmh, Controls());

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[Test]
	public void Tick_EmergencyHazardOnBeforeStop_Deducts10Once()
	{
		Begin(5, "on_emergency");

		Tick(0.5f, DrivingKmh, Controls(hazard: true));
		Tick(0.5f, DrivingKmh, Controls(hazard: true));
		Tick(1f, StoppedKmh, Controls(hazard: true));
		Tick(1f, StoppedKmh, Controls());
		Tick(1f, DrivingKmh, Controls());

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.EmergencyHazardEarly));
	}

	[Test]
	public void Tick_EmergencyHazardOnAfterDeadline_DeductsHazardLate()
	{
		StopForEmergency();

		Tick(2.9f, StoppedKmh, Controls());
		Tick(0.2f, StoppedKmh, Controls(hazard: true));

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.EmergencyHazardLate));
	}

	[Test]
	public void Tick_EmergencyDepartsBeforeHazardOn_DeductsHazardLate()
	{
		StopForEmergency();

		Tick(1f, DrivingKmh, Controls());

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.EmergencyHazardLate));
	}

	[Test]
	public void Tick_EmergencyDepartsWithHazardOn_Deducts10()
	{
		StopForEmergency();

		Tick(1f, StoppedKmh, Controls(hazard: true));
		Tick(1f, DrivingKmh, Controls(hazard: true));

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.EmergencyHazardNotOff));
	}

	[TestCase(true, 0)]
	[TestCase(false, 5)]
	public void BeginSection_Finish_DeductsWithoutApproachTurnSignal(bool hasApproachSignal, int expectedDeduction)
	{
		Begin(21, "hill_2");
		Tick(10f, DrivingKmh, Controls(rightSignal: hasApproachSignal));
		Tick(10f, DrivingKmh, Controls());

		Begin(22, "end");

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(expectedDeduction));
		if (expectedDeduction > 0)
		{
			Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.FinishSignalMissing));
			Assert.That(ScoreSheet.Deductions[0].PointNumber, Is.EqualTo(22));
		}
	}

	[Test]
	public void BeginSection_FinishWithoutVehicleState_NoDeduction()
	{
		Begin(21, "hill_2");
		evaluator.Tick(10f, DrivingKmh);

		Begin(22, "end");

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	#endregion

	#region Score Sheet

	[Test]
	public void ApplyDeduction_MoreThanMaxScore_ClampsScoreAtZero()
	{
		var scoreSheet = new DrivingScoreSheet(20, 10);

		scoreSheet.ApplyDeduction(new DrivingDeduction(0, "start", DrivingDeductionReason.LaneKeepingLineTouch, 15));
		scoreSheet.ApplyDeduction(new DrivingDeduction(0, "start", DrivingDeductionReason.LaneKeepingLineTouch, 15));

		Assert.That(scoreSheet.Score, Is.Zero);
		Assert.That(scoreSheet.TotalDeduction, Is.EqualTo(30));
	}

	[TestCase(0)]
	[TestCase(-5)]
	public void ApplyDeduction_NonPositivePoints_IsIgnored(int points)
	{
		var scoreSheet = new DrivingScoreSheet(100, 80);
		var deducted = new List<DrivingDeduction>();
		scoreSheet.Deducted += deducted.Add;

		scoreSheet.ApplyDeduction(new DrivingDeduction(0, "start", DrivingDeductionReason.LaneKeepingLineTouch, points));

		Assert.That(scoreSheet.Score, Is.EqualTo(100));
		Assert.That(scoreSheet.Deductions, Is.Empty);
		Assert.That(deducted, Is.Empty);
	}

	[Test]
	public void ApplyDeduction_SecondDisqualification_KeepsFirstReason()
	{
		var scoreSheet = new DrivingScoreSheet(100, 80);

		scoreSheet.ApplyDeduction(new DrivingDeduction(0, "start", DrivingDeductionReason.StartTimeOver, 0, true));
		scoreSheet.ApplyDeduction(new DrivingDeduction(1, "hill_0", DrivingDeductionReason.CourseTimeOver, 0, true));

		Assert.That(scoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.StartTimeOver));
		Assert.That(scoreSheet.Deductions.Count, Is.EqualTo(1));
	}

	#endregion
}
