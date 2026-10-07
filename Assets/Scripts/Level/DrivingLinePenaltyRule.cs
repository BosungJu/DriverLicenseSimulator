using System;
using System.Collections.Generic;
using UnityEngine;

// Line-touch penalty for sections running a given command (step suffix ignored, so "T_TEST" covers T_TEST_0..n).
[Serializable]
public class DrivingLinePenaltyRule
{
	[SerializeField] private string command;
	[SerializeField] private int lineTouchPenalty;

	public string Command => command;
	public int LineTouchPenalty => lineTouchPenalty;

	public DrivingLinePenaltyRule(string command, int lineTouchPenalty)
	{
		this.command = command;
		this.lineTouchPenalty = lineTouchPenalty;
	}

	// The first section command with a rule wins; sections without one use the lane-keeping default.
	public static int SelectPenalty(IReadOnlyList<string> sectionCommands, IReadOnlyList<DrivingLinePenaltyRule> rules,
		int defaultPenalty, out string matchedCommand)
	{
		matchedCommand = string.Empty;
		if (sectionCommands == null || rules == null)
		{
			return defaultPenalty;
		}

		foreach (string sectionCommand in sectionCommands)
		{
			DrivingSectionAction.SplitStep(sectionCommand, out string commandName, out _);
			foreach (DrivingLinePenaltyRule rule in rules)
			{
				if (rule != null && string.Equals(rule.Command?.Trim(), commandName, StringComparison.OrdinalIgnoreCase))
				{
					matchedCommand = sectionCommand;
					return rule.LineTouchPenalty;
				}
			}
		}

		return defaultPenalty;
	}
}
