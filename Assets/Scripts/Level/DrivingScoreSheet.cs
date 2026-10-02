using System;
using System.Collections.Generic;

// Items of the class 1/2 ordinary course test that can be judged from time, speed, and line contact.
public enum DrivingDeductionReason
{
	LaneKeepingLineTouch,
	ParkingLineTouch,
	ParkingTimeOver,
	StartTimeOver,
	HillNotStopped,
	HillShortStop,
	HillRollback,
	HillRollbackOver,
	HillPassTimeOver,
	SignalLongStop,
	SignalPassTimeOver,
	AccelerationTooSlow,
	OverSpeed,
	EmergencyStopLate,
	CourseTimeOver
}

public sealed class DrivingDeduction
{
	public int PointNumber { get; }
	public string Command { get; }
	public DrivingDeductionReason Reason { get; }
	public int Points { get; }
	public bool IsDisqualification { get; }

	public DrivingDeduction(int pointNumber, string command, DrivingDeductionReason reason, int points,
		bool isDisqualification = false)
	{
		PointNumber = pointNumber;
		Command = command;
		Reason = reason;
		Points = points;
		IsDisqualification = isDisqualification;
	}
}

// Plain score state so the scoring rules can be tested without a scene.
public sealed class DrivingScoreSheet
{
	private readonly List<DrivingDeduction> deductions = new List<DrivingDeduction>();

	public int MaxScore { get; }
	public int PassingScore { get; }
	public int TotalDeduction { get; private set; }
	public int Score => Math.Max(0, MaxScore - TotalDeduction);
	public DrivingDeduction Disqualification { get; private set; }
	public bool IsDisqualified => Disqualification != null;
	public bool IsPassed => !IsDisqualified && Score >= PassingScore;
	public IReadOnlyList<DrivingDeduction> Deductions => deductions;

	public event Action<DrivingDeduction> Deducted;

	public DrivingScoreSheet(int maxScore, int passingScore)
	{
		MaxScore = maxScore;
		PassingScore = passingScore;
	}

	public void ApplyDeduction(DrivingDeduction deduction)
	{
		if (deduction == null || (!deduction.IsDisqualification && deduction.Points <= 0))
		{
			return;
		}

		// The first disqualification decides the result; later ones would only repeat it.
		if (deduction.IsDisqualification && IsDisqualified)
		{
			return;
		}

		deductions.Add(deduction);
		TotalDeduction += Math.Max(0, deduction.Points);
		if (deduction.IsDisqualification)
		{
			Disqualification = deduction;
		}

		Deducted?.Invoke(deduction);
	}

	public int GetSectionDeduction(int pointNumber)
	{
		int sectionDeduction = 0;
		foreach (DrivingDeduction deduction in deductions)
		{
			if (deduction.PointNumber == pointNumber)
			{
				sectionDeduction += deduction.Points;
			}
		}

		return sectionDeduction;
	}

	public void Reset()
	{
		deductions.Clear();
		TotalDeduction = 0;
		Disqualification = null;
	}
}
