using System;
using UnityEngine;

public enum DrivingSectionState
{
    Ready,
    Active,
    Completed,
    Cancelled
}

// Each command has its own type so its judging rules can be added independently.
public abstract class DrivingSectionAction
{
    public const int NoStep = -1;
    private const char StepSeparator = '_';

    public string Command { get; }
    // Command without its "_n" step suffix, e.g. "hill" for "hill_0".
    public string CommandName { get; }
    // Detail step of a multi-point command such as hill_0..hill_2; NoStep when the command has no suffix.
    public int StepIndex { get; }
    public bool HasStep => StepIndex != NoStep;
    public DrivingSectionState State { get; private set; }
    public GameObject Vehicle { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public event Action<DrivingSectionAction> Entered;
    public event Action<DrivingSectionAction, float> Progressed;
    public event Action<DrivingSectionAction> Exited;
    public event Action<DrivingSectionAction> Cancelled;

    protected DrivingSectionAction(string command)
    {
        Command = command;
        SplitStep(command, out string commandName, out int stepIndex);
        CommandName = commandName;
        StepIndex = stepIndex;
    }

    internal void Activate(GameObject vehicle)
    {
        Vehicle = vehicle;
        ElapsedSeconds = 0f;
        State = DrivingSectionState.Active;
    }

    internal void NotifyEntered() => Entered?.Invoke(this);

    internal void Tick(float deltaSeconds)
    {
        if (State != DrivingSectionState.Active)
        {
            return;
        }

        ElapsedSeconds += deltaSeconds;
        Progressed?.Invoke(this, deltaSeconds);
    }

    internal void SetState(DrivingSectionState state)
    {
        State = state;
    }

    internal void NotifyStopped(bool cancelled)
    {
        if (cancelled)
        {
            Cancelled?.Invoke(this);
        }
        else
        {
            Exited?.Invoke(this);
        }
    }

    internal void Reset()
    {
        State = DrivingSectionState.Ready;
        Vehicle = null;
        ElapsedSeconds = 0f;
    }

    public static DrivingSectionAction Create(string command)
    {
        SplitStep(command, out string commandName, out _);
        switch (commandName.ToLowerInvariant())
        {
            case "start": return new StartSectionAction(command);
            case "hill": return new HillSectionAction(command);
            case "left": return new LeftTurnSectionAction(command);
            case "right": return new RightTurnSectionAction(command);
            case "signal": return new SignalSectionAction(command);
            case "t_test": return new ParkingSectionAction(command);
            case "fast": return new AccelerationSectionAction(command);
            case "on_emergency": return new EmergencySectionAction(command);
            case "end": return new FinishSectionAction(command);
            default: return new CustomSectionAction(command);
        }
    }

    // Only a trailing all-digit suffix is a step, so names such as "on_emergency" and "T_TEST" stay intact.
    public static void SplitStep(string command, out string commandName, out int stepIndex)
    {
        commandName = command.Trim();
        stepIndex = NoStep;

        int separatorIndex = commandName.LastIndexOf(StepSeparator);
        if (separatorIndex <= 0 || separatorIndex == commandName.Length - 1)
        {
            return;
        }

        string stepText = commandName.Substring(separatorIndex + 1);
        foreach (char character in stepText)
        {
            if (character < '0' || character > '9')
            {
                return;
            }
        }

        if (!int.TryParse(stepText, out int parsedStep))
        {
            return;
        }

        stepIndex = parsedStep;
        commandName = commandName.Substring(0, separatorIndex);
    }
}

public sealed class StartSectionAction : DrivingSectionAction
{
    public StartSectionAction(string command) : base(command) { }
}

public sealed class HillSectionAction : DrivingSectionAction
{
    public HillSectionAction(string command) : base(command) { }
}

public sealed class LeftTurnSectionAction : DrivingSectionAction
{
    public LeftTurnSectionAction(string command) : base(command) { }
}

public sealed class RightTurnSectionAction : DrivingSectionAction
{
    public RightTurnSectionAction(string command) : base(command) { }
}

public sealed class SignalSectionAction : DrivingSectionAction
{
    public SignalSectionAction(string command) : base(command) { }
}

public sealed class ParkingSectionAction : DrivingSectionAction
{
    public ParkingSectionAction(string command) : base(command) { }
}

public sealed class AccelerationSectionAction : DrivingSectionAction
{
    public AccelerationSectionAction(string command) : base(command) { }
}

public sealed class EmergencySectionAction : DrivingSectionAction
{
    public EmergencySectionAction(string command) : base(command) { }
}

public sealed class FinishSectionAction : DrivingSectionAction
{
    public FinishSectionAction(string command) : base(command) { }
}

public sealed class CustomSectionAction : DrivingSectionAction
{
    public CustomSectionAction(string command) : base(command) { }
}
