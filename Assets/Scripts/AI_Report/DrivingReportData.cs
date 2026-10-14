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
	public bool HasControlInput;
	public float SteeringInput;
	public float ThrottleInput;
	public bool IsBraking;
}

// Keep CSV input columns aligned with the AI project's schema feature names.
public static class DrivingReportCsv
{
	public const string Header =
		"time_s,point_number,speed_kmh,accel_mps2,yaw_rate_dps,heading_deg,pos_x,pos_z,steering_input,throttle_input,brake_applied";

	public static string Build(IReadOnlyList<DrivingSample> samples)
	{
		var csvBuilder = new StringBuilder();
		csvBuilder.AppendLine(Header);

		foreach (DrivingSample sample in samples)
		{
			csvBuilder.AppendLine(string.Format(CultureInfo.InvariantCulture,
				"{0:F3},{1},{2:F3},{3:F3},{4:F3},{5:F2},{6:F3},{7:F3},{8},{9},{10}",
				sample.TimeSeconds, sample.PointNumber, sample.SpeedKmh, sample.LongitudinalAccelerationMps2,
				sample.YawRateDegreesPerSecond, sample.HeadingDegrees, sample.PositionX, sample.PositionZ,
				sample.HasControlInput ? sample.SteeringInput.ToString("F3", CultureInfo.InvariantCulture) : string.Empty,
				sample.HasControlInput ? sample.ThrottleInput.ToString("F3", CultureInfo.InvariantCulture) : string.Empty,
				sample.HasControlInput ? (sample.IsBraking ? "1" : "0") : string.Empty));
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
