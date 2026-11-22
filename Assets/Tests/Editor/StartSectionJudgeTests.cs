using System;
using System.Linq;
using NUnit.Framework;

public class StartSectionJudgeTests
{
	private DrivingScoreSheet scoreSheet;
	private StartSectionJudge judge;
	private PairRandom random;

	#region Setup and Helpers

	private sealed class PairRandom : Random
	{
		private readonly int first;
		private readonly int second;
		public int Calls { get; private set; }

		public PairRandom(int first, int second)
		{
			this.first = first;
			this.second = second;
		}

		public override int Next(int minValue, int maxValue)
		{
			return Calls++ % 2 == 0 ? first : second;
		}
	}

	private void CreateJudge(int first = 0, int second = 1)
	{
		scoreSheet = new DrivingScoreSheet(100, 80);
		random = new PairRandom(first, second);
		judge = new StartSectionJudge(new DrivingScoringSettings(), new DrivingDeductionRecorder(scoreSheet), random);
	}

	private static DrivingVehicleState Controls(bool headlight = false, bool wiper = false,
		VehicleGear gear = VehicleGear.Park, bool parkingBrake = false, bool left = false,
		bool right = false, bool hazard = false, bool highBeam = false)
	{
		return new DrivingVehicleState(true, true, hazard, left, right, parkingBrake, headlight, wiper, gear, highBeam);
	}

	private void CompleteRequiredInstructions()
	{
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.SeatBelt));
		judge.TickVehicleControls(0.1f, 0f, Controls());
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.Engine));
		judge.TickVehicleControls(0.1f, 0f, Controls());
	}

	private static DrivingDeductionReason Reason(DrivingStartInstruction instruction)
	{
		switch (instruction)
		{
			case DrivingStartInstruction.Headlight: return DrivingDeductionReason.HeadlightNotOperated;
			case DrivingStartInstruction.Wiper: return DrivingDeductionReason.WiperNotOperated;
			case DrivingStartInstruction.Gear: return DrivingDeductionReason.GearNotOperated;
			case DrivingStartInstruction.TurnSignal: return DrivingDeductionReason.StartTurnSignalNotOperated;
			default: throw new ArgumentOutOfRangeException(nameof(instruction));
		}
	}

	private static DrivingVehicleState OperatedControls(DrivingStartInstruction instruction)
	{
		switch (instruction)
		{
			case DrivingStartInstruction.Headlight: return Controls(headlight: true);
			case DrivingStartInstruction.Wiper: return Controls(wiper: true);
			case DrivingStartInstruction.Gear: return Controls(gear: VehicleGear.Neutral, parkingBrake: true);
			case DrivingStartInstruction.TurnSignal: return Controls(left: true);
			default: throw new ArgumentOutOfRangeException(nameof(instruction));
		}
	}

	#endregion

	#region Selection and Scoring

	[Test]
	public void Selection_AllDraws_ProduceTwelveDistinctOrderedPairsWithoutDuplicates()
	{
		var pairs = new System.Collections.Generic.HashSet<string>();
		for (int first = 0; first < 4; first++)
		{
			for (int second = 1; second < 4; second++)
			{
				CreateJudge(first, second);
				CompleteRequiredInstructions();
				DrivingStartInstruction firstInstruction = judge.CurrentInstruction;
				judge.TickVehicleControls(5.1f, 0f, Controls());
				DrivingStartInstruction secondInstruction = judge.CurrentInstruction;
				Assert.That(secondInstruction, Is.Not.EqualTo(firstInstruction));
				pairs.Add(firstInstruction + "/" + secondInstruction);

				judge.TickVehicleControls(5.1f, 0f, Controls());
				Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.Depart));
				Assert.That(scoreSheet.Deductions.Select(d => d.Reason),
					Is.EqualTo(new[] { Reason(firstInstruction), Reason(secondInstruction) }));
				Assert.That(scoreSheet.TotalDeduction, Is.EqualTo(10));
				Assert.That(random.Calls, Is.EqualTo(2));
			}
		}
		Assert.That(pairs.Count, Is.EqualTo(12));
	}

	[TestCase(0, 1)]
	[TestCase(1, 2)]
	[TestCase(2, 3)]
	[TestCase(3, 1)]
	public void Tick_CompletedSelectedTargets_OnlyAdvancesOneInstructionPerTick(int first, int second)
	{
		CreateJudge(first, second);
		CompleteRequiredInstructions();
		DrivingStartInstruction firstInstruction = judge.CurrentInstruction;
		judge.TickVehicleControls(5f, 0f, OperatedControls(firstInstruction));
		Assert.That(judge.CurrentInstruction, Is.Not.EqualTo(firstInstruction));
		Assert.That(judge.CurrentInstruction, Is.Not.EqualTo(DrivingStartInstruction.Depart));
		judge.TickVehicleControls(5f, 0f, OperatedControls(judge.CurrentInstruction));
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.Depart));
		judge.TickVehicleControls(0.1f, 10f, Controls(left: true));
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.None));
		Assert.That(scoreSheet.Deductions, Is.Empty);
	}

	[TestCase(0, DrivingDeductionReason.HeadlightNotOperated)]
	[TestCase(1, DrivingDeductionReason.WiperNotOperated)]
	[TestCase(2, DrivingDeductionReason.GearNotOperated)]
	[TestCase(3, DrivingDeductionReason.StartTurnSignalNotOperated)]
	public void Tick_TargetReachedAfterDeadline_DeductsAndDoesNotRedraw(int first, DrivingDeductionReason reason)
	{
		CreateJudge(first, 1);
		CompleteRequiredInstructions();
		DrivingStartInstruction instruction = judge.CurrentInstruction;
		judge.TickVehicleControls(5f, 0f, Controls());
		Assert.That(judge.CurrentInstruction, Is.EqualTo(instruction));
		Assert.That(scoreSheet.Deductions, Is.Empty);
		judge.TickVehicleControls(0.1f, 0f, Controls(true, true, VehicleGear.Neutral, true, true));
		Assert.That(scoreSheet.Deductions.Single().Reason, Is.EqualTo(reason));
		Assert.That(random.Calls, Is.EqualTo(2));
	}

	[Test]
	public void Tick_DepartingBeforeInstructions_DeductsOnlyBothSelectedDevicesOnce()
	{
		CreateJudge(2, 3);
		judge.TickVehicleControls(0.1f, 10f, Controls(left: true));
		judge.TickVehicleControls(10f, 10f, Controls());
		Assert.That(scoreSheet.Deductions.Select(d => d.Reason), Is.EqualTo(new[]
		{
			DrivingDeductionReason.GearNotOperated, DrivingDeductionReason.StartTurnSignalNotOperated
		}));
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.None));
	}

	[Test]
	public void Reset_PartiallyCompletedRun_ClearsProgressPenaltiesAndDrawsAgain()
	{
		var seededRandom = new Random(1234);
		var evaluator = new DrivingTestScoreEvaluator(new DrivingScoringSettings(), seededRandom);
		var pairs = new System.Collections.Generic.HashSet<string>();
		for (int run = 0; run < 20; run++)
		{
			evaluator.Reset();
			Assert.That(evaluator.CurrentStartInstruction, Is.EqualTo(DrivingStartInstruction.None));
			Assert.That(evaluator.ScoreSheet.Deductions, Is.Empty);
			evaluator.BeginSection(0, new[] { "start" });
			Assert.That(evaluator.CurrentStartInstruction, Is.EqualTo(DrivingStartInstruction.SeatBelt));
			evaluator.Tick(0.1f, 0f, vehicleState: Controls());
			evaluator.Tick(0.1f, 0f, vehicleState: Controls());
			string first = evaluator.CurrentStartInstruction.ToString();
			evaluator.Tick(5.1f, 0f, vehicleState: Controls());
			pairs.Add(first + "/" + evaluator.CurrentStartInstruction);
			Assert.That(evaluator.ScoreSheet.TotalDeduction, Is.EqualTo(5));
		}
		Assert.That(pairs.Count, Is.GreaterThan(1));
	}

	#endregion

	#region Exact Targets

	[TestCase(VehicleGear.Park, true, 0f)]
	[TestCase(VehicleGear.Reverse, true, 0f)]
	[TestCase(VehicleGear.Drive, true, 0f)]
	[TestCase(VehicleGear.Neutral, false, 0f)]
	[TestCase(VehicleGear.Neutral, true, 2f)]
	public void Tick_GearWithoutSafeNeutralTarget_DoesNotComplete(VehicleGear gear, bool parkingBrake, float speedKmh)
	{
		CreateJudge(2, 1);
		CompleteRequiredInstructions();
		// Engine off excludes departure so the speed prerequisite can be tested independently.
		var controls = new DrivingVehicleState(true, false, false, false, false, parkingBrake, gear: gear);
		judge.TickVehicleControls(0.1f, speedKmh, controls);
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.Gear));
	}

	[TestCase(false, false, false)]
	[TestCase(false, true, false)]
	[TestCase(true, false, true)]
	[TestCase(true, true, false)]
	public void Tick_WrongSignalOrHazards_DoesNotComplete(bool left, bool right, bool hazard)
	{
		CreateJudge(3, 1);
		CompleteRequiredInstructions();
		judge.TickVehicleControls(0.1f, 0f, Controls(left: left, right: right, hazard: hazard));
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.TurnSignal));
	}

	[Test]
	public void Tick_HighBeamOn_DoesNotCompleteLowBeamInstruction()
	{
		CreateJudge();
		CompleteRequiredInstructions();
		judge.TickVehicleControls(0.1f, 0f, Controls(headlight: true, highBeam: true));
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.Headlight));
		judge.TickVehicleControls(0.1f, 0f, Controls(headlight: true));
		Assert.That(judge.CurrentInstruction, Is.EqualTo(DrivingStartInstruction.Wiper));
	}

	#endregion
}
