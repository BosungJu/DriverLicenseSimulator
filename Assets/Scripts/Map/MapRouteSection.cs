using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class MapRouteSection : MonoBehaviour
{
    [SerializeField] private int routeIndex;
    [SerializeField] private int pointNumber;
    [SerializeField] private Vector2 cadPosition;
    [SerializeField] private string instructionText;

    private IReadOnlyList<DrivingSectionAction> actions = Array.Empty<DrivingSectionAction>();
    private int lifecycleVersion;

    public int RouteIndex => routeIndex;
    public int PointNumber => pointNumber;
    public Vector2 CadPosition => cadPosition;
    public string InstructionText => instructionText;
    public IReadOnlyList<string> Commands { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<DrivingSectionAction> Actions => actions;
    public DrivingSectionState State { get; private set; }

    public event Action<MapRouteSection> Entered;
    public event Action<MapRouteSection, float> Progressed;
    public event Action<MapRouteSection> Exited;
    public event Action<MapRouteSection> Cancelled;

    public void Initialize(int index, MapData point, MapRouteInstruction instruction)
    {
        ResetSection();
        routeIndex = index;
        pointNumber = instruction != null ? instruction.PointNumber : index;
        cadPosition = new Vector2(point.PosX, point.PosY);
        instructionText = instruction != null ? instruction.Text : string.Empty;
        Commands = instruction != null ? instruction.Commands : Array.Empty<string>();
        var sectionActions = new List<DrivingSectionAction>(Commands.Count);
        foreach (string command in Commands)
        {
            sectionActions.Add(DrivingSectionAction.Create(command));
        }

        actions = sectionActions.AsReadOnly();
    }

    public void Enter(GameObject vehicle)
    {
        if (State != DrivingSectionState.Ready || vehicle == null)
        {
            return;
        }

        State = DrivingSectionState.Active;
        int version = ++lifecycleVersion;
        foreach (DrivingSectionAction action in actions)
        {
            action.Activate(vehicle);
        }

        Entered?.Invoke(this);
        foreach (DrivingSectionAction action in actions)
        {
            if (version != lifecycleVersion)
            {
                return;
            }
            action.NotifyEntered();
        }
    }

    public void Tick(float deltaSeconds)
    {
        if (State != DrivingSectionState.Active || deltaSeconds < 0f)
        {
            return;
        }

        int version = lifecycleVersion;
        foreach (DrivingSectionAction action in actions)
        {
            action.Tick(deltaSeconds);
            if (version != lifecycleVersion)
            {
                return;
            }
        }

        Progressed?.Invoke(this, deltaSeconds);
    }

    public void Complete() => StopSection(false);
    public void Cancel() => StopSection(true);

    public void ResetSection()
    {
        Cancel();
        lifecycleVersion++;
        State = DrivingSectionState.Ready;
        foreach (DrivingSectionAction action in actions)
        {
            action.Reset();
        }
    }

    private void StopSection(bool cancelled)
    {
        if (State != DrivingSectionState.Active)
        {
            return;
        }

        State = cancelled ? DrivingSectionState.Cancelled : DrivingSectionState.Completed;
        int version = ++lifecycleVersion;
        foreach (DrivingSectionAction action in actions)
        {
            action.SetState(State);
        }

        foreach (DrivingSectionAction action in actions)
        {
            action.NotifyStopped(cancelled);
            if (version != lifecycleVersion)
            {
                return;
            }
        }

        if (cancelled)
        {
            Cancelled?.Invoke(this);
        }
        else
        {
            Exited?.Invoke(this);
        }
    }

    private void OnDisable() => Cancel();
}
