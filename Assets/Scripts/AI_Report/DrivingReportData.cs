using System;
using System.Collections.Generic;

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
