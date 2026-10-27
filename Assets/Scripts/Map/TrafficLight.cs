using System;
using UnityEngine;

public enum TrafficLightState
{
	Green,
	Yellow,
	Red
}

// Cycles a traffic light through green, yellow and red, lighting only the lamp of the current state.
[DisallowMultipleComponent]
public class TrafficLight : MonoBehaviour
{
	// Child object names in Resources/TrafficLight/TrafficLight.prefab, used to bind lamps when the component is added.
	private const string RedLampName = "RedLight";
	private const string YellowLampName = "YellowLight";
	private const string GreenLampName = "GreenLight";

	// Prevents a zero-length phase from stalling the cycle loop, in seconds.
	private const float MinPhaseDuration = 0.1f;

	private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
	private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

	[Header("Lamps")]
	[SerializeField] private Renderer redLamp;
	[SerializeField] private Renderer yellowLamp;
	[SerializeField] private Renderer greenLamp;
	[Tooltip("Base color multiplier for lamps that are off.")]
	[SerializeField, Range(0f, 1f)] private float offLampBrightness = 0.15f;
	[Tooltip("Emission intensity for the lit lamp. Visible only when the lamp material has Emission enabled.")]
	[SerializeField, Min(0f)] private float onLampEmissionIntensity = 2f;

	[Header("Cycle")]
	[SerializeField, Min(MinPhaseDuration)] private float greenDuration = 10f;
	[SerializeField, Min(MinPhaseDuration)] private float yellowDuration = 3f;
	[SerializeField, Min(MinPhaseDuration)] private float redDuration = 10f;
	[SerializeField] private TrafficLightState initialState = TrafficLightState.Red;
	[Tooltip("Seconds already spent in the initial state at start. Use it to offset lights at the same intersection.")]
	[SerializeField, Min(0f)] private float initialElapsedSeconds;
	[Tooltip("When off, the light keeps its state until SetState is called.")]
	[SerializeField] private bool cycleAutomatically = true;

	private MaterialPropertyBlock lampPropertyBlock;
	private Color redLitColor;
	private Color yellowLitColor;
	private Color greenLitColor;
	private float elapsedStateSeconds;

	public TrafficLightState CurrentState { get; private set; }
	public float RemainingStateSeconds => Mathf.Max(0f, GetDuration(CurrentState) - elapsedStateSeconds);

	// Turned off by TrafficSignalController, which then sets every state through SetState.
	public bool CycleAutomatically
	{
		get => cycleAutomatically;
		set => cycleAutomatically = value;
	}

	public event Action<TrafficLightState> StateChanged;

	// Called when the component is added in the Editor, so the prefab lamps are bound without manual dragging.
	private void Reset()
	{
		redLamp = FindLamp(RedLampName);
		yellowLamp = FindLamp(YellowLampName);
		greenLamp = FindLamp(GreenLampName);
	}

	private void Awake()
	{
		lampPropertyBlock = new MaterialPropertyBlock();
		redLitColor = GetLitColor(redLamp);
		yellowLitColor = GetLitColor(yellowLamp);
		greenLitColor = GetLitColor(greenLamp);

		// Whole cycles are dropped first so a large offset needs at most one pass through the phases.
		float cycleDuration = GetDuration(TrafficLightState.Green) + GetDuration(TrafficLightState.Yellow) + GetDuration(TrafficLightState.Red);
		CurrentState = initialState;
		elapsedStateSeconds = Mathf.Repeat(initialElapsedSeconds, cycleDuration);
		AdvanceExpiredStates();
		ApplyLamps();
	}

	private void Update()
	{
		if (!cycleAutomatically)
		{
			return;
		}

		elapsedStateSeconds += Time.deltaTime;
		AdvanceExpiredStates();
	}

	// Forces a state and restarts its timer, e.g. for a scripted test scenario.
	public void SetState(TrafficLightState state)
	{
		elapsedStateSeconds = 0f;
		ChangeState(state);
	}

	// A frame hitch can span several phases, so advance until the current phase has time left.
	private void AdvanceExpiredStates()
	{
		while (elapsedStateSeconds >= GetDuration(CurrentState))
		{
			elapsedStateSeconds -= GetDuration(CurrentState);
			ChangeState(GetNextState(CurrentState));
		}
	}

	private void ChangeState(TrafficLightState state)
	{
		CurrentState = state;
		ApplyLamps();
		StateChanged?.Invoke(CurrentState);
	}

	private void ApplyLamps()
	{
		ApplyLamp(redLamp, redLitColor, CurrentState == TrafficLightState.Red);
		ApplyLamp(yellowLamp, yellowLitColor, CurrentState == TrafficLightState.Yellow);
		ApplyLamp(greenLamp, greenLitColor, CurrentState == TrafficLightState.Green);
	}

	private void ApplyLamp(Renderer lamp, Color litColor, bool isLit)
	{
		if (lamp == null)
		{
			return;
		}

		Color baseColor = isLit ? litColor : litColor * offLampBrightness;
		baseColor.a = litColor.a;
		Color emissionColor = isLit ? litColor * onLampEmissionIntensity : Color.black;

		lamp.GetPropertyBlock(lampPropertyBlock);
		lampPropertyBlock.SetColor(BaseColorId, baseColor);
		lampPropertyBlock.SetColor(EmissionColorId, emissionColor);
		lamp.SetPropertyBlock(lampPropertyBlock);
	}

	private float GetDuration(TrafficLightState state)
	{
		switch (state)
		{
			case TrafficLightState.Green:
				return Mathf.Max(MinPhaseDuration, greenDuration);
			case TrafficLightState.Yellow:
				return Mathf.Max(MinPhaseDuration, yellowDuration);
			default:
				return Mathf.Max(MinPhaseDuration, redDuration);
		}
	}

	private static TrafficLightState GetNextState(TrafficLightState state)
	{
		switch (state)
		{
			case TrafficLightState.Green:
				return TrafficLightState.Yellow;
			case TrafficLightState.Yellow:
				return TrafficLightState.Red;
			default:
				return TrafficLightState.Green;
		}
	}

	private static Color GetLitColor(Renderer lamp)
	{
		if (lamp == null || lamp.sharedMaterial == null || !lamp.sharedMaterial.HasProperty(BaseColorId))
		{
			return Color.white;
		}

		return lamp.sharedMaterial.GetColor(BaseColorId);
	}

	private Renderer FindLamp(string lampName)
	{
		foreach (Renderer childRenderer in GetComponentsInChildren<Renderer>(true))
		{
			if (childRenderer.name == lampName)
			{
				return childRenderer;
			}
		}

		Debug.LogWarning($"[TrafficLight] {name}: lamp '{lampName}' was not found in children.", this);
		return null;
	}
}
