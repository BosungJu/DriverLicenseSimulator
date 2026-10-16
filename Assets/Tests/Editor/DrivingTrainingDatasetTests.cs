using System;
using System.Globalization;
using NUnit.Framework;

public class DrivingTrainingDatasetTests
{
	private const float SampleRateHz = 50f;
	private const float StepSeconds = 1f / SampleRateHz;

	private const string ExpectedFeatureNames =
		"[\"speed_kmh\",\"accel_mps2\",\"yaw_rate_dps\",\"steering_input\",\"throttle_input\",\"brake_input\"," +
		"\"gear_park\",\"gear_reverse\",\"gear_neutral\",\"gear_drive\",\"parking_brake_on\"," +
		"\"engine_on\",\"left_signal_on\",\"right_signal_on\",\"hazard_on\",\"headlight_on\",\"high_beam_on\"," +
		"\"seat_belt_on\",\"horn_on\"]";

	#region Normal Cases

	[Test]
	public void Build_TwoSections_WritesOneRecordPerSectionWithFeaturesInSchemaOrder()
	{
		DrivingSample first = CreateUsableSample(0, 1);
		first.SpeedKmh = 4f;
		first.LongitudinalAccelerationMps2 = -0.5f;
		first.YawRateDegreesPerSecond = 3f;
		first.SteeringInput = -0.25f;
		first.ThrottleInput = 0.5f;
		first.BrakeInput = 1f;
		first.IsLeftSignalOn = true;
		first.IsHeadlightOn = true;
		first.IsSeatBeltOn = true;
		DrivingSample second = CreateUsableSample(1, 1);
		second.Gear = VehicleGear.Reverse;
		second.IsParkingBrakeOn = true;
		DrivingSample third = CreateUsableSample(2, 2);
		third.IsHazardOn = true;
		third.IsHighBeamOn = true;
		third.IsHornOn = true;

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { first, second, third }, "run-1", null, SampleRateHz);

		string[] lines = SplitLines(result.JsonLines);
		Assert.AreEqual(2, result.WrittenSectionCount);
		Assert.AreEqual(0, result.SkippedSectionCount);
		Assert.AreEqual(
			"{\"schema_version\":1,\"sample_rate_hz\":50,\"run_id\":\"run-1\",\"section_id\":1,\"start_time_s\":0.000," +
			"\"feature_names\":" + ExpectedFeatureNames + ",\"features\":[" +
			"[4.000,-0.500,3.000,-0.250,0.500,1.000,0,0,0,1,0,1,1,0,0,1,0,1,0]," +
			"[0.000,0.000,0.000,0.000,0.000,0.000,0,1,0,0,1,1,0,0,0,0,0,0,0]]}",
			lines[0]);
		StringAssert.Contains("\"section_id\":2,\"start_time_s\":0.040,", lines[1]);
		StringAssert.EndsWith("\"features\":[[0.000,0.000,0.000,0.000,0.000,0.000,0,0,0,1,0,1,0,0,1,0,1,0,1]]}", lines[1]);
	}

	[Test]
	public void Build_CommaDecimalCulture_WritesInvariantNumbers()
	{
		DrivingSample sample = CreateUsableSample(0, 1);
		sample.SpeedKmh = 1.5f;
		CultureInfo originalCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

			DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { sample }, "run", null, SampleRateHz);

			StringAssert.Contains("[[1.500,0.000,", result.JsonLines);
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	[Test]
	public void Build_DriverId_WritesTrimmedEscapedDriverId()
	{
		DrivingSample sample = CreateUsableSample(0, 1);

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { sample }, "run\"1", " driver\\A ", SampleRateHz);

		StringAssert.Contains("\"run_id\":\"run\\\"1\",\"driver_id\":\"driver\\\\A\",", result.JsonLines);
	}

	[Test]
	public void Build_NoDriverId_OmitsDriverIdField()
	{
		DrivingSample sample = CreateUsableSample(0, 1);

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { sample }, "run", "  ", SampleRateHz);

		StringAssert.DoesNotContain("driver_id", result.JsonLines);
	}

	#endregion

	#region Skipped Sections

	[Test]
	public void Build_SectionWithoutControlInput_SkipsOnlyThatSection()
	{
		DrivingSample missingInput = CreateUsableSample(0, 1);
		missingInput.HasControlInput = false;
		DrivingSample usable = CreateUsableSample(1, 2);

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { missingInput, usable }, "run", null, SampleRateHz);

		Assert.AreEqual(1, result.WrittenSectionCount);
		Assert.AreEqual(1, result.SkippedSectionCount);
		StringAssert.Contains("\"section_id\":2,", result.JsonLines);
		StringAssert.DoesNotContain("\"section_id\":1,", result.JsonLines);
	}

	[Test]
	public void Build_SectionWithoutVehicleSystemState_SkipsSection()
	{
		DrivingSample sample = CreateUsableSample(0, 1);
		sample.HasVehicleSystemState = false;

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { sample }, "run", null, SampleRateHz);

		Assert.AreEqual(0, result.WrittenSectionCount);
		Assert.AreEqual(1, result.SkippedSectionCount);
		Assert.AreEqual(string.Empty, result.JsonLines);
	}

	[TestCase(0.04f)]
	[TestCase(0f)]
	[TestCase(-0.02f)]
	[TestCase(float.NaN)]
	public void Build_SecondSampleNotOneStepLater_SkipsSection(float secondSampleTimeSeconds)
	{
		DrivingSample first = CreateUsableSample(0, 1);
		DrivingSample second = CreateUsableSample(1, 1);
		second.TimeSeconds = secondSampleTimeSeconds;

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { first, second }, "run", null, SampleRateHz);

		Assert.AreEqual(0, result.WrittenSectionCount);
		Assert.AreEqual(1, result.SkippedSectionCount);
	}

	[Test]
	public void Build_NonFiniteSectionStartTime_SkipsSectionInsteadOfWritingInvalidJson()
	{
		DrivingSample sample = CreateUsableSample(0, 1);
		sample.TimeSeconds = float.NaN;

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { sample }, "run", null, SampleRateHz);

		Assert.AreEqual(1, result.SkippedSectionCount);
		Assert.AreEqual(string.Empty, result.JsonLines);
	}

	[Test]
	public void Build_NonFiniteFeature_SkipsSection()
	{
		DrivingSample sample = CreateUsableSample(0, 1);
		sample.YawRateDegreesPerSecond = float.NaN;

		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(new[] { sample }, "run", null, SampleRateHz);

		Assert.AreEqual(1, result.SkippedSectionCount);
	}

	[Test]
	public void Build_EmptySamples_WritesNothing()
	{
		DrivingTrainingDatasetResult result = DrivingTrainingDataset.Build(Array.Empty<DrivingSample>(), "run", null, SampleRateHz);

		Assert.AreEqual(string.Empty, result.JsonLines);
		Assert.AreEqual(0, result.WrittenSectionCount);
		Assert.AreEqual(0, result.SkippedSectionCount);
	}

	#endregion

	#region Invalid Arguments

	[TestCase(null)]
	[TestCase(" ")]
	public void Build_EmptyRunId_Throws(string runId)
	{
		Assert.Throws<ArgumentException>(() => DrivingTrainingDataset.Build(Array.Empty<DrivingSample>(), runId, null, SampleRateHz));
	}

	[TestCase(0f)]
	[TestCase(float.NaN)]
	public void Build_InvalidSampleRate_Throws(float sampleRateHz)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => DrivingTrainingDataset.Build(Array.Empty<DrivingSample>(), "run", null, sampleRateHz));
	}

	#endregion

	#region Test Helpers

	// A sample with every value available, gear D, engine on and all other systems off.
	private static DrivingSample CreateUsableSample(int stepIndex, int pointNumber)
	{
		return new DrivingSample
		{
			TimeSeconds = stepIndex * StepSeconds,
			PointNumber = pointNumber,
			HasControlInput = true,
			Gear = VehicleGear.Drive,
			HasVehicleSystemState = true,
			IsEngineOn = true
		};
	}

	private static string[] SplitLines(string jsonLines)
	{
		return jsonLines.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
	}

	#endregion
}
