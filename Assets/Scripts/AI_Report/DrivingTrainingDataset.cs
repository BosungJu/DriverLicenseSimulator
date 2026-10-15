using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Result of converting one recorded session into training records.
public sealed class DrivingTrainingDatasetResult
{
	public DrivingTrainingDatasetResult(string jsonLines, int writtenSectionCount, int skippedSectionCount)
	{
		JsonLines = jsonLines;
		WrittenSectionCount = writtenSectionCount;
		SkippedSectionCount = skippedSectionCount;
	}

	public string JsonLines { get; }
	public int WrittenSectionCount { get; }
	public int SkippedSectionCount { get; }
}

// Builds unlabeled section records of the AI project's data contract v1 (one JSON object per line, T x F features).
// Labels and split are assigned offline by reviewers, so only features and run metadata are written here.
public static class DrivingTrainingDataset
{
	public const int SchemaVersion = 1;

	// Order must match feature_names in the AI project's res/schema.example.json.
	public static readonly IReadOnlyList<string> FeatureNames = new[]
	{
		"speed_kmh", "accel_mps2", "yaw_rate_dps",
		"steering_input", "throttle_input", "brake_applied",
		"engine_on", "left_signal_on", "right_signal_on", "hazard_on", "headlight_on", "high_beam_on"
	};

	// Intervals outside this many physics steps mean dropped (too long) or duplicated (too short) frames.
	private const float MinSampleIntervalInSteps = 0.5f;
	private const float MaxSampleIntervalInSteps = 1.5f;

	public static DrivingTrainingDatasetResult Build(IReadOnlyList<DrivingSample> samples, string runId,
		string anonymousDriverId, float sampleRateHz)
	{
		if (samples == null)
		{
			throw new ArgumentNullException(nameof(samples));
		}

		if (string.IsNullOrWhiteSpace(runId))
		{
			throw new ArgumentException("Run ID must not be empty.", nameof(runId));
		}

		if (float.IsNaN(sampleRateHz) || float.IsInfinity(sampleRateHz) || sampleRateHz <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(sampleRateHz), "Sample rate must be finite and positive.");
		}

		float minSampleIntervalSeconds = MinSampleIntervalInSteps / sampleRateHz;
		float maxSampleIntervalSeconds = MaxSampleIntervalInSteps / sampleRateHz;
		var jsonBuilder = new StringBuilder();
		int writtenSectionCount = 0;
		int skippedSectionCount = 0;
		int sectionStartIndex = 0;

		// Each consecutive run of one point number is one section record.
		for (int i = 1; i <= samples.Count; i++)
		{
			bool isSectionEnd = i == samples.Count || samples[i].PointNumber != samples[sectionStartIndex].PointNumber;
			if (!isSectionEnd)
			{
				continue;
			}

			if (IsSectionUsable(samples, sectionStartIndex, i, minSampleIntervalSeconds, maxSampleIntervalSeconds))
			{
				AppendSectionRecord(jsonBuilder, samples, sectionStartIndex, i, runId, anonymousDriverId, sampleRateHz);
				writtenSectionCount++;
			}
			else
			{
				skippedSectionCount++;
			}

			sectionStartIndex = i;
		}

		return new DrivingTrainingDatasetResult(jsonBuilder.ToString(), writtenSectionCount, skippedSectionCount);
	}

	// The AI loader rejects missing or non-finite values and cannot detect dropped frames, so such sections are skipped.
	private static bool IsSectionUsable(IReadOnlyList<DrivingSample> samples, int startIndex, int endIndex,
		float minSampleIntervalSeconds, float maxSampleIntervalSeconds)
	{
		for (int i = startIndex; i < endIndex; i++)
		{
			DrivingSample sample = samples[i];
			if (!sample.HasControlInput || !sample.HasVehicleSystemState)
			{
				return false;
			}

			if (!IsFinite(sample.TimeSeconds) || sample.TimeSeconds < 0f || !IsFinite(sample.SpeedKmh) || !IsFinite(sample.LongitudinalAccelerationMps2) ||
				!IsFinite(sample.YawRateDegreesPerSecond) || !IsFinite(sample.SteeringInput) || !IsFinite(sample.ThrottleInput))
			{
				return false;
			}

			if (i == startIndex)
			{
				continue;
			}

			float sampleIntervalSeconds = sample.TimeSeconds - samples[i - 1].TimeSeconds;
			bool isFixedStepInterval = sampleIntervalSeconds >= minSampleIntervalSeconds &&
				sampleIntervalSeconds <= maxSampleIntervalSeconds;
			if (!isFixedStepInterval)
			{
				return false;
			}
		}

		return true;
	}

	private static void AppendSectionRecord(StringBuilder jsonBuilder, IReadOnlyList<DrivingSample> samples,
		int startIndex, int endIndex, string runId, string anonymousDriverId, float sampleRateHz)
	{
		jsonBuilder.Append("{\"schema_version\":").Append(SchemaVersion);
		jsonBuilder.Append(",\"sample_rate_hz\":").Append(sampleRateHz.ToString("0.###", CultureInfo.InvariantCulture));
		jsonBuilder.Append(",\"run_id\":");
		AppendJsonString(jsonBuilder, runId);
		if (!string.IsNullOrWhiteSpace(anonymousDriverId))
		{
			jsonBuilder.Append(",\"driver_id\":");
			AppendJsonString(jsonBuilder, anonymousDriverId.Trim());
		}

		jsonBuilder.Append(",\"section_id\":").Append(samples[startIndex].PointNumber);
		jsonBuilder.Append(",\"start_time_s\":").Append(FormatContinuous(samples[startIndex].TimeSeconds));

		jsonBuilder.Append(",\"feature_names\":[");
		for (int i = 0; i < FeatureNames.Count; i++)
		{
			if (i > 0)
			{
				jsonBuilder.Append(',');
			}

			AppendJsonString(jsonBuilder, FeatureNames[i]);
		}

		jsonBuilder.Append("],\"features\":[");
		for (int i = startIndex; i < endIndex; i++)
		{
			if (i > startIndex)
			{
				jsonBuilder.Append(',');
			}

			AppendFeatureFrame(jsonBuilder, samples[i]);
		}

		jsonBuilder.Append("]}\n");
	}

	// Values use the same rounding as driving_log.csv so training and session inference see identical inputs.
	private static void AppendFeatureFrame(StringBuilder jsonBuilder, DrivingSample sample)
	{
		jsonBuilder.Append('[')
			.Append(FormatContinuous(sample.SpeedKmh)).Append(',')
			.Append(FormatContinuous(sample.LongitudinalAccelerationMps2)).Append(',')
			.Append(FormatContinuous(sample.YawRateDegreesPerSecond)).Append(',')
			.Append(FormatContinuous(sample.SteeringInput)).Append(',')
			.Append(FormatContinuous(sample.ThrottleInput)).Append(',')
			.Append(FormatFlag(sample.IsBraking)).Append(',')
			.Append(FormatFlag(sample.IsEngineOn)).Append(',')
			.Append(FormatFlag(sample.IsLeftSignalOn)).Append(',')
			.Append(FormatFlag(sample.IsRightSignalOn)).Append(',')
			.Append(FormatFlag(sample.IsHazardOn)).Append(',')
			.Append(FormatFlag(sample.IsHeadlightOn)).Append(',')
			.Append(FormatFlag(sample.IsHighBeamOn))
			.Append(']');
	}

	private static string FormatContinuous(float value)
	{
		return value.ToString("F3", CultureInfo.InvariantCulture);
	}

	private static char FormatFlag(bool value)
	{
		return value ? '1' : '0';
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	private static void AppendJsonString(StringBuilder jsonBuilder, string value)
	{
		jsonBuilder.Append('"');
		foreach (char character in value)
		{
			switch (character)
			{
				case '"':
					jsonBuilder.Append("\\\"");
					break;
				case '\\':
					jsonBuilder.Append("\\\\");
					break;
				default:
					if (character < ' ')
					{
						jsonBuilder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
					}
					else
					{
						jsonBuilder.Append(character);
					}

					break;
			}
		}

		jsonBuilder.Append('"');
	}
}
