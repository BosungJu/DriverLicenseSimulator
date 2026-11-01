// Judges the scoring rules that belong to one kind of course section.
// The evaluator creates judges when a section begins and drops them when it ends,
// so section state starts fresh without explicit clearing. Course-wide rules stay in the evaluator.
public abstract class DrivingSectionJudge
{
	private readonly DrivingDeductionRecorder recorder;

	protected DrivingScoringSettings Settings { get; }

	protected DrivingSectionJudge(DrivingScoringSettings settings, DrivingDeductionRecorder recorder)
	{
		Settings = settings;
		this.recorder = recorder;
	}

	// Runs once after the section became current.
	public virtual void Begin()
	{
	}

	// routeDistanceMeters is the vehicle position along the section's route direction (larger = further ahead).
	// approachSignalState is the light of the stop line the vehicle is approaching, or null when none governs it.
	public virtual void Tick(float deltaSeconds, float speedKmh, float routeDistanceMeters,
		TrafficLightState? approachSignalState)
	{
	}

	// Only called while the vehicle controls can be read.
	public virtual void TickVehicleControls(float deltaSeconds, float speedKmh, DrivingVehicleState vehicleState)
	{
	}

	// Runs before the section is left, so deductions belong to the ending section.
	public virtual void End()
	{
	}

	protected bool IsStopped(float speedKmh)
	{
		return speedKmh <= Settings.StoppedSpeedKmh;
	}

	// Movement with the engine off (e.g. settling after the spawn drop) is not the driver departing.
	protected bool IsDriving(float speedKmh, DrivingVehicleState vehicleState)
	{
		return vehicleState.IsEngineOn && !IsStopped(speedKmh);
	}

	protected void Deduct(DrivingDeductionReason reason, int points, string command)
	{
		recorder.Deduct(reason, points, command);
	}

	protected void Disqualify(DrivingDeductionReason reason, string command)
	{
		recorder.Disqualify(reason, command);
	}
}
