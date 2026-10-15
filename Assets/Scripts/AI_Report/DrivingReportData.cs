using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// One physics-step sample exported to driving_log.csv; the Python side picks the LSTM features from its columns.
public struct DrivingSample
{
	public float TimeSeconds;
	public int PointNumber;
	public float SpeedKmh;
	public float LongitudinalAccelerationMps2;
	public float YawRateDegreesPerSecond;
	public float HeadingDegrees;
	public float PositionX;
	public float PositionZ;
	// CarController values read in this physics step.
	public bool HasControlInput;
	public float SteeringInput;
	public float ThrottleInput;
	public float BrakeInput;
	public VehicleGear Gear;
	public bool IsParkingBrakeOn;
	// VehicleSystems toggles; they change in Update, so a sample holds the latest state before this physics step.
	public bool HasVehicleSystemState;
	public bool IsEngineOn;
	public bool IsLeftSignalOn;
	public bool IsRightSignalOn;
	public bool IsHazardOn;
	public bool IsHeadlightOn;
	public bool IsHighBeamOn;
	public bool IsSeatBeltOn;
	public bool IsHornOn;
}

// Model feature columns shared by driving_log.csv and training_sections.jsonl.
// Keep names and order aligned with feature_names in the AI project's res/schema.example.json.
public static class DrivingFeatureColumns
{
	public static readonly IReadOnlyList<string> Names = new[]
	{
		"speed_kmh", "accel_mps2", "yaw_rate_dps",
		"steering_input", "throttle_input", "brake_input",
		"gear_park", "gear_reverse", "gear_neutral", "gear_drive", "parking_brake_on",
		"engine_on", "left_signal_on", "right_signal_on", "hazard_on", "headlight_on", "high_beam_on",
		"seat_belt_on", "horn_on"
	};

	// Appends the values in Names order, comma separated. Unavailable values stay blank so the AI side
	// rejects them instead of reading a fabricated 0.
	public static void AppendValues(StringBuilder builder, DrivingSample sample)
	{
		bool hasControl = sample.HasControlInput;
		bool hasSystems = sample.HasVehicleSystemState;
		string[] values =
		{
			FormatContinuous(true, sample.SpeedKmh),
			FormatContinuous(true, sample.LongitudinalAccelerationMps2),
			FormatContinuous(true, sample.YawRateDegreesPerSecond),
			FormatContinuous(hasControl, sample.SteeringInput),
			FormatContinuous(hasControl, sample.ThrottleInput),
			FormatContinuous(hasControl, sample.BrakeInput),
			FormatFlag(hasControl, sample.Gear == VehicleGear.Park),
			FormatFlag(hasControl, sample.Gear == VehicleGear.Reverse),
			FormatFlag(hasControl, sample.Gear == VehicleGear.Neutral),
			FormatFlag(hasControl, sample.Gear == VehicleGear.Drive),
			FormatFlag(hasControl, sample.IsParkingBrakeOn),
			FormatFlag(hasSystems, sample.IsEngineOn),
			FormatFlag(hasSystems, sample.IsLeftSignalOn),
			FormatFlag(hasSystems, sample.IsRightSignalOn),
			FormatFlag(hasSystems, sample.IsHazardOn),
			FormatFlag(hasSystems, sample.IsHeadlightOn),
			FormatFlag(hasSystems, sample.IsHighBeamOn),
			FormatFlag(hasSystems, sample.IsSeatBeltOn),
			FormatFlag(hasSystems, sample.IsHornOn)
		};

		builder.Append(string.Join(",", values));
	}

	private static string FormatContinuous(bool isAvailable, float value)
	{
		return isAvailable ? value.ToString("F3", CultureInfo.InvariantCulture) : string.Empty;
	}

	private static string FormatFlag(bool isAvailable, bool value)
	{
		if (!isAvailable)
		{
			return string.Empty;
		}

		return value ? "1" : "0";
	}
}

// driving_log.csv: time and position metadata followed by the model feature columns.
public static class DrivingReportCsv
{
	public static readonly string Header =
		"time_s,point_number,heading_deg,pos_x,pos_z," + string.Join(",", DrivingFeatureColumns.Names);

	public static string Build(IReadOnlyList<DrivingSample> samples)
	{
		var csvBuilder = new StringBuilder();
		csvBuilder.AppendLine(Header);

		foreach (DrivingSample sample in samples)
		{
			csvBuilder.Append(string.Format(CultureInfo.InvariantCulture, "{0:F3},{1},{2:F2},{3:F3},{4:F3},",
				sample.TimeSeconds, sample.PointNumber, sample.HeadingDegrees, sample.PositionX, sample.PositionZ));
			DrivingFeatureColumns.AppendValues(csvBuilder, sample);
			csvBuilder.AppendLine();
		}

		return csvBuilder.ToString();
	}
}

// JSON layout of section_results.json, read by the Python report pipeline.
[Serializable]
public class DrivingSectionResultFile
{
	public string recordedAt;
	public int score;
	public int maxScore;
	public int passingScore;
	public int totalDeduction;
	public bool isPassed;
	public bool isDisqualified;
	public float recordedSeconds;
	public float sampleRateHz;
	public List<DrivingSectionInfo> sections = new List<DrivingSectionInfo>();
	public List<DrivingDeductionRecord> deductions = new List<DrivingDeductionRecord>();
}

[Serializable]
public class DrivingSectionInfo
{
	public int pointNumber;
	public string instructionText;
	public List<string> commands = new List<string>();
	public int sampleCount;
}

[Serializable]
public class DrivingDeductionRecord
{
	public int pointNumber;
	public string command;
	public string reason;
	public int points;
	public bool isDisqualification;
}
