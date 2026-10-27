using System;
using System.Collections.Generic;
using UnityEngine;

// Lights of one group show the same state, e.g. the two opposite approaches of a straight road.
[Serializable]
public class TrafficSignalGroup
{
	[SerializeField] private List<TrafficLight> lights = new List<TrafficLight>();

	public IReadOnlyList<TrafficLight> Lights => lights;
}

// Runs the lights of one intersection so that only one group is green or yellow at a time.
// Each group goes green -> yellow -> red, then every light stays red for the clearance time before the next group.
[DisallowMultipleComponent]
public class TrafficSignalController : MonoBehaviour
{
	// Prevents a zero-length green or yellow phase from stalling the phase loop, in seconds.
	private const float MinPhaseDuration = 0.1f;

	private enum SignalPhase
	{
		Green,
		Yellow,
		AllRed
	}

	[SerializeField] private List<TrafficSignalGroup> groups = new List<TrafficSignalGroup>();

	[Header("Cycle")]
	[SerializeField, Min(MinPhaseDuration)] private float greenDuration = 10f;
	[SerializeField, Min(MinPhaseDuration)] private float yellowDuration = 3f;
	[Tooltip("Every light stays red this long between groups, so the intersection clears, in seconds.")]
	[SerializeField, Min(0f)] private float allRedDuration = 2f;
	[SerializeField, Min(0)] private int startGroupIndex;

	private int activeGroupIndex;
	private SignalPhase phase;
	private float elapsedPhaseSeconds;
	private bool isRunning;

	// Start runs after every TrafficLight.Awake, so the lights are ready to receive states.
	private void Start()
	{
		if (groups.Count == 0)
		{
			Debug.LogWarning($"[TrafficSignalController] {name}: no signal groups, so nothing is controlled.", this);
			return;
		}

		WarnDuplicateLights();
		TakeControlOfLights();

		activeGroupIndex = startGroupIndex % groups.Count;
		elapsedPhaseSeconds = 0f;
		isRunning = true;
		ApplyPhase(SignalPhase.Green);
	}

	private void Update()
	{
		if (!isRunning)
		{
			return;
		}

		elapsedPhaseSeconds += Time.deltaTime;

		// A frame hitch can span several phases, so advance until the current phase has time left.
		while (elapsedPhaseSeconds >= GetDuration(phase))
		{
			elapsedPhaseSeconds -= GetDuration(phase);
			AdvancePhase();
		}
	}

	private void AdvancePhase()
	{
		switch (phase)
		{
			case SignalPhase.Green:
				ApplyPhase(SignalPhase.Yellow);
				break;
			case SignalPhase.Yellow:
				ApplyPhase(SignalPhase.AllRed);
				break;
			default:
				activeGroupIndex = (activeGroupIndex + 1) % groups.Count;
				ApplyPhase(SignalPhase.Green);
				break;
		}
	}

	// Inactive groups are set red first, so no two groups are ever green or yellow together.
	private void ApplyPhase(SignalPhase nextPhase)
	{
		phase = nextPhase;

		for (int i = 0; i < groups.Count; i++)
		{
			if (i != activeGroupIndex || phase == SignalPhase.AllRed)
			{
				SetGroupState(groups[i], TrafficLightState.Red);
			}
		}

		if (phase == SignalPhase.Green)
		{
			SetGroupState(groups[activeGroupIndex], TrafficLightState.Green);
		}
		else if (phase == SignalPhase.Yellow)
		{
			SetGroupState(groups[activeGroupIndex], TrafficLightState.Yellow);
		}
	}

	private float GetDuration(SignalPhase signalPhase)
	{
		switch (signalPhase)
		{
			case SignalPhase.Green:
				return Mathf.Max(MinPhaseDuration, greenDuration);
			case SignalPhase.Yellow:
				return Mathf.Max(MinPhaseDuration, yellowDuration);
			default:
				return Mathf.Max(0f, allRedDuration);
		}
	}

	// The controller owns the timing, so each light stops running its own cycle.
	private void TakeControlOfLights()
	{
		foreach (TrafficSignalGroup group in groups)
		{
			foreach (TrafficLight trafficLight in group.Lights)
			{
				if (trafficLight != null)
				{
					trafficLight.CycleAutomatically = false;
				}
			}
		}
	}

	private static void SetGroupState(TrafficSignalGroup group, TrafficLightState state)
	{
		foreach (TrafficLight trafficLight in group.Lights)
		{
			if (trafficLight != null && trafficLight.CurrentState != state)
			{
				trafficLight.SetState(state);
			}
		}
	}

	// A light in two groups would be switched by both and could show green with a crossing group.
	private void WarnDuplicateLights()
	{
		var assignedLights = new HashSet<TrafficLight>();
		foreach (TrafficSignalGroup group in groups)
		{
			foreach (TrafficLight trafficLight in group.Lights)
			{
				if (trafficLight != null && !assignedLights.Add(trafficLight))
				{
					Debug.LogWarning($"[TrafficSignalController] {name}: '{trafficLight.name}' is in more than one group.", trafficLight);
				}
			}
		}
	}
}
