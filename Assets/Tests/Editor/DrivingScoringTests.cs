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

	[Test]
	public void Tick_HillStoppedThreeSecondsThenDeparts_NoDeduction()
	{
		StopOnHill(3f);

		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.1f);
		evaluator.EndSection();

		Assert.That(ScoreSheet.Deductions, Is.Empty);
	}

	[Test]
	public void Tick_HillDepartsBeforeThreeSeconds_Deducts10()
	{
		StopOnHill(2f);

		evaluator.Tick(0.1f, 5f, HillStopDistance + 0.1f);

		Assert.That(ScoreSheet.TotalDeduction, Is.EqualTo(10));
		Assert.That(ScoreSheet.Deductions[0].Reason, Is.EqualTo(DrivingDeductionReason.HillShortStop));
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

	[Test]
	public void Tick_SignalNotPassedWithin30Seconds_Disqualifies()
	{
		Begin(9, "right", "signal");

		evaluator.Tick(30f, 10f);
		Assert.That(ScoreSheet.IsDisqualified, Is.False);

		evaluator.Tick(0.1f, 10f);

		Assert.That(ScoreSheet.Disqualification.Reason, Is.EqualTo(DrivingDeductionReason.SignalPassTimeOver));
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
