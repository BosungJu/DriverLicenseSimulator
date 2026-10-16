using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class DrivingTestProgressEvent : UnityEvent<int, int>
{
}

[System.Serializable]
public class DrivingTestInstructionEvent : UnityEvent<int, string>
{
}

[DisallowMultipleComponent]
[RequireComponent(typeof(MapGenerator))]
public class DrivingTestLevel : MonoBehaviour
{
    // CAD point numbers start at 0, so "no point entered yet" needs a distinct value.
    public const int NoCurrentPoint = -1;

    public int PassedRoutePointCount => nextRoutePointIndex;
    public bool IsCompleted => isCompleted;
    public MapRouteSection CurrentSection { get; private set; }
    // Vehicle that entered the current section; null until the first route point is reached.
    public GameObject ActiveVehicle { get; private set; }
    public int CurrentPointNumber { get; private set; } = NoCurrentPoint;
    public IReadOnlyList<string> CurrentInstructions { get; private set; } = System.Array.Empty<string>();
    // Sends the CAD point number (starting at 0) and one command per invocation.
    public DrivingTestInstructionEvent OnInstructionStarted => onInstructionStarted;
    public DrivingTestProgressEvent OnRouteProgress => onRouteProgress;
    public UnityEvent<GameObject> OnLevelCompleted => onLevelCompleted;
    // Raised after progress is cleared, so dependent state such as the score can restart with the course.
    public event System.Action LevelReset;

    [SerializeField] private MapGenerator mapGenerator;
    [SerializeField] private GameObject testVehicle;
    [SerializeField] private bool generateMapOnAwake = true;
    [SerializeField] private DrivingTestProgressEvent onRouteProgress = new DrivingTestProgressEvent();
    [SerializeField] private UnityEvent<GameObject> onLevelCompleted = new UnityEvent<GameObject>();
    [SerializeField] private DrivingTestInstructionEvent onInstructionStarted = new DrivingTestInstructionEvent();

    private int nextRoutePointIndex;
    private bool isCompleted;
    private int progressVersion;
    private bool isResetting;

    private void Awake()
    {
        CacheMapGenerator();
        ResetLevel();

        if (generateMapOnAwake && mapGenerator != null)
        {
            mapGenerator.GenerateMap();
        }
    }

    private void OnEnable()
    {
        CacheMapGenerator();
        if (mapGenerator == null)
        {
            Debug.LogWarning("DrivingTestLevel: MapGenerator reference is missing.", this);
            return;
        }

        if (CurrentSection != null && CurrentSection.State == DrivingSectionState.Cancelled)
        {
            ResetLevel();
        }

        mapGenerator.SubscribeRoutePoint(HandleRoutePointEnter);
        mapGenerator.MapGenerated += ResetLevel;
    }

    private void OnDisable()
    {
        progressVersion++;
        if (CurrentSection != null)
        {
            CurrentSection.Cancel();
        }

        if (mapGenerator != null)
        {
            mapGenerator.UnsubscribeRoutePoint(HandleRoutePointEnter);
            mapGenerator.MapGenerated -= ResetLevel;
        }
    }

    public void ResetLevel()
    {
        if (isResetting)
        {
            return;
        }

        isResetting = true;
        try
        {
            ResetProgress();
        }
        finally
        {
            isResetting = false;
        }

        LevelReset?.Invoke();
    }

    private void ResetProgress()
    {
        progressVersion++;
        MapRouteSection previousSection = CurrentSection;
        CurrentSection = null;
        if (previousSection != null)
        {
            previousSection.Cancel();
        }

        if (mapGenerator != null)
        {
            foreach (MapRouteSection section in mapGenerator.RouteSections)
            {
                if (section != null)
                {
                    section.ResetSection();
                }
            }
        }

        nextRoutePointIndex = 0;
        isCompleted = false;
        CurrentPointNumber = NoCurrentPoint;
        ActiveVehicle = null;
        CurrentInstructions = System.Array.Empty<string>();
        progressVersion++;
    }

    // Starts the course for a vehicle placed directly on the first route point.
    // A vehicle teleported inside a trigger it already overlapped gets no new trigger enter, so this enters explicitly.
    public void EnterFirstRoutePoint(GameObject vehicle)
    {
        HandleRoutePointEnter(0, vehicle);
    }

    private void Update()
    {
        if (CurrentSection != null && !isCompleted)
        {
            CurrentSection.Tick(Time.deltaTime);
        }
    }

    private void HandleRoutePointEnter(int routePointIndex, GameObject otherObject)
    {
        if (!isActiveAndEnabled || isResetting || isCompleted
            || routePointIndex != nextRoutePointIndex || !IsTestVehicle(otherObject))
        {
            return;
        }

        if (!string.IsNullOrEmpty(mapGenerator.RouteInstructionError)
            || routePointIndex >= mapGenerator.RouteSections.Count)
        {
            return;
        }

        int transitionVersion = ++progressVersion;
        if (CurrentSection != null)
        {
            CurrentSection.Complete();
            if (progressVersion != transitionVersion)
            {
                return;
            }
        }

        CurrentSection = mapGenerator.RouteSections[routePointIndex];
        CurrentPointNumber = CurrentSection.PointNumber;
        CurrentInstructions = CurrentSection.Commands;
        ActiveVehicle = otherObject;

        nextRoutePointIndex++;
        int enteredProgressVersion = ++progressVersion;
        CurrentSection.Enter(otherObject);
        if (progressVersion != enteredProgressVersion)
        {
            return;
        }

        if (CurrentInstructions.Count > 0)
        {
            Debug.Log($"DrivingTestLevel: Point {CurrentPointNumber} started [{string.Join(" | ", CurrentInstructions)}].", this);
        }

        onRouteProgress.Invoke(nextRoutePointIndex, mapGenerator.RoutePointCount);

        // Publish every command in this group in the same frame, with the full group already active.
        for (int i = 0; i < CurrentInstructions.Count; i++)
        {
            if (progressVersion != enteredProgressVersion)
            {
                return;
            }

            onInstructionStarted.Invoke(CurrentPointNumber, CurrentInstructions[i]);
        }

        if (progressVersion != enteredProgressVersion)
        {
            return;
        }

        if (mapGenerator.RoutePointCount == 0 || nextRoutePointIndex < mapGenerator.RoutePointCount)
        {
            return;
        }

        isCompleted = true;
        CurrentSection.Complete();
        if (progressVersion != enteredProgressVersion)
        {
            return;
        }

        onLevelCompleted.Invoke(otherObject);
    }

    public bool IsTestVehicle(GameObject otherObject)
    {
        if (otherObject == null)
        {
            return false;
        }

        if (testVehicle == null)
        {
            return true;
        }

        return otherObject == testVehicle
            || otherObject.transform.IsChildOf(testVehicle.transform)
            || testVehicle.transform.IsChildOf(otherObject.transform);
    }

    private void CacheMapGenerator()
    {
        if (mapGenerator == null)
        {
            TryGetComponent(out mapGenerator);
        }
    }
}
