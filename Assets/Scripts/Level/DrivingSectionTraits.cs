using System;
using System.Collections.Generic;

// Which section scoring rules the commands of one route section turn on.
// The evaluator creates its section judges from these traits, and ReportManager exports the same traits as the
// section features of every sample, so the AI model sees the section context its deduction labels were judged in.
public readonly struct DrivingSectionTraits
{
	public bool IsStart { get; }
	// Excludes the exit step, which only judges the exit speed of the preceding run.
	public bool IsAcceleration { get; }
	public bool IsEmergency { get; }
	// Only the hill_n step configured as the stop zone.
	public bool IsHillStop { get; }
	public bool IsSignal { get; }
	public bool IsLeftTurn { get; }
	public bool IsRightTurn { get; }
	// Excludes the terminal T_TEST step, which closes the parking course at entry.
	public bool IsParking { get; }
	public bool IsFinish { get; }

	private DrivingSectionTraits(bool isStart, bool isAcceleration, bool isEmergency, bool isHillStop, bool isSignal,
		bool isLeftTurn, bool isRightTurn, bool isParking, bool isFinish)
	{
		IsStart = isStart;
		IsAcceleration = isAcceleration;
		IsEmergency = isEmergency;
		IsHillStop = isHillStop;
		IsSignal = isSignal;
		IsLeftTurn = isLeftTurn;
		IsRightTurn = isRightTurn;
		IsParking = isParking;
		IsFinish = isFinish;
	}

	public static DrivingSectionTraits FromCommands(IReadOnlyList<string> commands, int hillStopStep)
	{
		IReadOnlyList<string> sectionCommands = commands ?? Array.Empty<string>();
		bool isAcceleration = HasCommand(sectionCommands, DrivingScoringSettings.AccelerationCommand)
			&& !HasCommandStep(sectionCommands, DrivingScoringSettings.AccelerationCommand, DrivingScoringSettings.AccelerationExitStep);
		bool isParking = HasCommand(sectionCommands, DrivingScoringSettings.ParkingCommand)
			&& !HasCommandStep(sectionCommands, DrivingScoringSettings.ParkingCommand, DrivingScoringSettings.ParkingExitStep);

		return new DrivingSectionTraits(
			HasCommand(sectionCommands, DrivingScoringSettings.StartCommand),
			isAcceleration,
			HasCommand(sectionCommands, DrivingScoringSettings.EmergencyCommand),
			HasCommandStep(sectionCommands, DrivingScoringSettings.HillCommand, hillStopStep),
			HasCommand(sectionCommands, DrivingScoringSettings.SignalCommand),
			HasCommand(sectionCommands, DrivingScoringSettings.LeftTurnCommand),
			HasCommand(sectionCommands, DrivingScoringSettings.RightTurnCommand),
			isParking,
			HasCommand(sectionCommands, DrivingScoringSettings.FinishCommand));
	}

	private static bool HasCommand(IReadOnlyList<string> commands, string commandName)
	{
		foreach (string command in commands)
		{
			DrivingSectionAction.SplitStep(command, out string name, out _);
			if (string.Equals(name, commandName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasCommandStep(IReadOnlyList<string> commands, string commandName, int stepIndex)
	{
		foreach (string command in commands)
		{
			DrivingSectionAction.SplitStep(command, out string name, out int step);
			if (step == stepIndex && string.Equals(name, commandName, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
