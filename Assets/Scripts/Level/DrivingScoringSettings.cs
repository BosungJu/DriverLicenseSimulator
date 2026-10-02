using System;
using System.Collections.Generic;
using UnityEngine;

// Defaults follow the class 1/2 ordinary course test scoring table.
[Serializable]
public class DrivingScoringSettings
{
	public const string ParkingCommand = "T_TEST";
	public const string StartCommand = "start";
	public const string AccelerationCommand = "fast";
	public const string EmergencyCommand = "on_emergency";
	public const string HillCommand = "hill";
	public const string SignalCommand = "signal";

	[Header("Result")]
	[SerializeField] private int maxScore = 100;
	[SerializeField] private int passingScore = 80;

	[Header("Line Touch")]
	[Tooltip("차로 준수: a wheel touching a lane line from start to finish.")]
	[SerializeField] private int laneKeepingPenalty = 15;
	[Tooltip("Section-specific line penalties. 직각주차: each wheel touch of a detection line.")]
	[SerializeField] private List<DrivingLinePenaltyRule> lineTouchRules = new List<DrivingLinePenaltyRule>
	{
		new DrivingLinePenaltyRule(ParkingCommand, 10)
	};
	[Tooltip("Contacts within this time count as one touch, so collider jitter is not deducted repeatedly.")]
	[SerializeField] private float lineTouchCooldownSeconds = 1f;

	[Header("출발")]
	[Tooltip("Disqualified when the start line is not passed within this time.")]
	[SerializeField] private float startTimeLimitSeconds = 30f;

	[Header("경사로 정지·출발")]
	[Tooltip("hill_n step whose section is the stop zone.")]
	[SerializeField] private int hillStopStep = 1;
	[SerializeField] private float hillMinimumStopSeconds = 3f;
	[SerializeField] private int hillShortStopPenalty = 10;
	[SerializeField] private float hillRollbackPenaltyMeters = 0.5f;
	[SerializeField] private int hillRollbackPenalty = 10;
	[Tooltip("Disqualified when rolling back this far after stopping.")]
	[SerializeField] private float hillRollbackDisqualifyMeters = 1f;
	[Tooltip("Disqualified when the stop zone is not passed within this time after stopping.")]
	[SerializeField] private float hillPassTimeLimitSeconds = 30f;

	[Header("신호교차로")]
	[SerializeField] private float signalStopLimitSeconds = 20f;
	[SerializeField] private int signalStopPenalty = 5;
	[Tooltip("Disqualified when the intersection is not passed within this time.")]
	[SerializeField] private float signalPassTimeLimitSeconds = 30f;

	[Header("직각주차")]
	[SerializeField] private float parkingTimeLimitSeconds = 120f;
	[SerializeField] private int parkingTimeOverPenalty = 10;

	[Header("속도가속 구간")]
	[SerializeField] private float accelerationTargetKmh = 20f;
	[SerializeField] private int accelerationPenalty = 10;

	[Header("지정속도 유지")]
	[SerializeField] private float speedLimitKmh = 20f;
	[SerializeField] private float overSpeedIntervalSeconds = 3f;
	[SerializeField] private int overSpeedPenalty = 3;

	[Header("돌발상황 급정지")]
	[SerializeField] private float emergencyStopTimeLimitSeconds = 2f;
	[Tooltip("Speed at or below this counts as stopped.")]
	[SerializeField] private float stoppedSpeedKmh = 1f;
	[SerializeField] private int emergencyPenalty = 10;

	[Header("전체 지정시간")]
	[Tooltip("Designated time for the whole course (9 min 28 s in the scoring table).")]
	[SerializeField] private float courseTimeLimitSeconds = 568f;
	[SerializeField] private float courseTimeOverIntervalSeconds = 5f;
	[SerializeField] private int courseTimeOverPenalty = 3;

	public int MaxScore => maxScore;
	public int PassingScore => passingScore;
	public int LaneKeepingPenalty => laneKeepingPenalty;
	public IReadOnlyList<DrivingLinePenaltyRule> LineTouchRules => lineTouchRules;
	public float LineTouchCooldownSeconds => lineTouchCooldownSeconds;
	public float StartTimeLimitSeconds => startTimeLimitSeconds;
	public int HillStopStep => hillStopStep;
	public float HillMinimumStopSeconds => hillMinimumStopSeconds;
	public int HillShortStopPenalty => hillShortStopPenalty;
	public float HillRollbackPenaltyMeters => hillRollbackPenaltyMeters;
	public int HillRollbackPenalty => hillRollbackPenalty;
	public float HillRollbackDisqualifyMeters => hillRollbackDisqualifyMeters;
	public float HillPassTimeLimitSeconds => hillPassTimeLimitSeconds;
	public float SignalStopLimitSeconds => signalStopLimitSeconds;
	public int SignalStopPenalty => signalStopPenalty;
	public float SignalPassTimeLimitSeconds => signalPassTimeLimitSeconds;
	public float ParkingTimeLimitSeconds => parkingTimeLimitSeconds;
	public int ParkingTimeOverPenalty => parkingTimeOverPenalty;
	public float AccelerationTargetKmh => accelerationTargetKmh;
	public int AccelerationPenalty => accelerationPenalty;
	public float SpeedLimitKmh => speedLimitKmh;
	public float OverSpeedIntervalSeconds => overSpeedIntervalSeconds;
	public int OverSpeedPenalty => overSpeedPenalty;
	public float EmergencyStopTimeLimitSeconds => emergencyStopTimeLimitSeconds;
	public float StoppedSpeedKmh => stoppedSpeedKmh;
	public int EmergencyPenalty => emergencyPenalty;
	public float CourseTimeLimitSeconds => courseTimeLimitSeconds;
	public float CourseTimeOverIntervalSeconds => courseTimeOverIntervalSeconds;
	public int CourseTimeOverPenalty => courseTimeOverPenalty;
}
