using System;
using System.Collections.Generic;
using UnityEngine;

// Stop line of one approach lane at a signalized intersection, reusing a painted segment of the map's Stopline layer.
// Add it next to the TrafficLight that the lane follows (one component per governed stop line).
// At runtime it binds to the Stopline segment nearest to stopLineApproachPoint and reports vehicles crossing that segment.
public class TrafficSignalStopLine : MonoBehaviour
{
	// The approach point must be at least this far off the line to tell the approach side from the intersection side, in meters.
	private const float MinApproachSideOffsetMeters = 0.05f;
	private const float ApproachPointGizmoRadius = 0.3f;
	private const float TravelDirectionGizmoLength = 2f;

	[SerializeField] private TrafficLight trafficLight;
	[SerializeField] private MapGenerator mapGenerator;

	[Header("Stop Line")]
	[Tooltip("World point just before the painted stop line, on the lane that waits at this light. "
		+ "The nearest Stopline segment is bound, and the direction from this point to the line is entering the intersection.")]
	[SerializeField] private Vector3 stopLineApproachPoint;
	[Tooltip("A Stopline segment farther than this from the approach point is not bound, in meters.")]
	[SerializeField, Min(0f)] private float maxBindDistanceMeters = 3f;
	[Tooltip("A vehicle within this distance before the line is waiting at this light, in meters.")]
	[SerializeField, Min(0f)] private float approachDistanceMeters = 30f;

	// Overlapping collider count per vehicle, so the body colliders of one vehicle cross once.
	private readonly Dictionary<GameObject, int> overlapCounts = new Dictionary<GameObject, int>();
	private BoxCollider boundSegment;
	private MapLineSegmentSensor boundSensor;
	// Horizontal frame of the bound segment, cached at bind time because generated map lines do not move.
	private Vector3 lineCenter;
	private Vector3 alongLineDirection;
	private Vector3 travelDirection;
	private float halfLineLength;
	private bool hasStarted;

	public TrafficLight TrafficLight => trafficLight;
	public TrafficLightState SignalState => trafficLight.CurrentState;
	public bool IsBound => boundSegment != null;

	// Raised when a vehicle's body first reaches the line while moving toward the intersection.
	public event Action<TrafficSignalStopLine, GameObject> Crossed;

	private void Reset()
	{
		trafficLight = GetComponent<TrafficLight>();
		stopLineApproachPoint = transform.position;
	}

	private void OnEnable()
	{
		// The map is regenerated in play (e.g. DrivingTestLevel.Awake), which replaces the Stopline segments.
		if (mapGenerator != null)
		{
			mapGenerator.MapGenerated += BindStopLineSegment;
		}

		if (hasStarted)
		{
			BindStopLineSegment();
		}
	}

	// Start runs after every MapLayerCollisionRelay.Awake, so the Stopline segment triggers already exist.
	private void Start()
	{
		hasStarted = true;

		if (trafficLight == null || mapGenerator == null)
		{
			Debug.LogWarning($"[TrafficSignalStopLine] {name}: TrafficLight or MapGenerator is not assigned, so crossings are not reported.", this);
			return;
		}

		BindStopLineSegment();
	}

	private void OnDisable()
	{
		if (mapGenerator != null)
		{
			mapGenerator.MapGenerated -= BindStopLineSegment;
		}

		UnbindStopLineSegment();
	}

	// True when the position is in this lane, before the line and within the approach distance.
	public bool IsApproachedFrom(Vector3 worldPosition)
	{
		if (trafficLight == null || boundSegment == null)
		{
			return false;
		}

		Vector3 offsetFromLine = Vector3.ProjectOnPlane(worldPosition - lineCenter, Vector3.up);
		float distanceBeforeLine = -Vector3.Dot(offsetFromLine, travelDirection);
		float lateralOffset = Mathf.Abs(Vector3.Dot(offsetFromLine, alongLineDirection));

		return distanceBeforeLine > 0f && distanceBeforeLine <= approachDistanceMeters && lateralOffset <= halfLineLength;
	}

	private void BindStopLineSegment()
	{
		UnbindStopLineSegment();
		if (trafficLight == null || mapGenerator == null)
		{
			return;
		}

		if (!mapGenerator.TryGetLayerRelay(MapGenerator.StoplineLayerName, out MapLayerCollisionRelay stoplineRelay))
		{
			Debug.LogWarning($"[TrafficSignalStopLine] {name}: the map has no '{MapGenerator.StoplineLayerName}' layer to bind.", this);
			return;
		}

		BoxCollider nearestSegment = FindNearestSegment(stoplineRelay.LineTriggers, out float distanceToSegment);
		if (nearestSegment == null || distanceToSegment > maxBindDistanceMeters)
		{
			Debug.LogWarning($"[TrafficSignalStopLine] {name}: no Stopline segment within {maxBindDistanceMeters} m of the approach point {stopLineApproachPoint}.", this);
			return;
		}

		GetSegmentFrame(nearestSegment, out Vector3 segmentCenter, out Vector3 segmentDirection, out float segmentHalfLength);
		Vector3 acrossLineDirection = Vector3.Cross(Vector3.up, segmentDirection);
		float approachSideOffset = Vector3.Dot(Vector3.ProjectOnPlane(stopLineApproachPoint - segmentCenter, Vector3.up), acrossLineDirection);
		if (Mathf.Abs(approachSideOffset) < MinApproachSideOffsetMeters)
		{
			Debug.LogWarning($"[TrafficSignalStopLine] {name}: the approach point is on the line, so the approach side is unknown. Move it before the line.", this);
			return;
		}

		boundSegment = nearestSegment;
		lineCenter = segmentCenter;
		alongLineDirection = segmentDirection;
		halfLineLength = segmentHalfLength;
		// The approach point is before the line, so traffic moves from its side toward the other side.
		travelDirection = approachSideOffset > 0f ? -acrossLineDirection : acrossLineDirection;

		if (!nearestSegment.TryGetComponent(out boundSensor))
		{
			boundSensor = nearestSegment.gameObject.AddComponent<MapLineSegmentSensor>();
		}

		boundSensor.TriggerEntered += HandleSegmentEntered;
		boundSensor.TriggerExited += HandleSegmentExited;
	}

	// The sensor stays on the segment because another stop line may share it; it is destroyed with the generated layer.
	private void UnbindStopLineSegment()
	{
		if (boundSensor != null)
		{
			boundSensor.TriggerEntered -= HandleSegmentEntered;
			boundSensor.TriggerExited -= HandleSegmentExited;
		}

		boundSensor = null;
		boundSegment = null;
		overlapCounts.Clear();
	}

	private BoxCollider FindNearestSegment(IReadOnlyList<BoxCollider> segments, out float nearestDistance)
	{
		BoxCollider nearestSegment = null;
		nearestDistance = float.PositiveInfinity;

		foreach (BoxCollider segment in segments)
		{
			if (segment == null)
			{
				continue;
			}

			GetSegmentFrame(segment, out Vector3 segmentCenter, out Vector3 segmentDirection, out float segmentHalfLength);
			Vector3 offsetFromCenter = Vector3.ProjectOnPlane(stopLineApproachPoint - segmentCenter, Vector3.up);
			float alongLineOffset = Mathf.Clamp(Vector3.Dot(offsetFromCenter, segmentDirection), -segmentHalfLength, segmentHalfLength);
			float distanceToSegment = Vector3.Distance(offsetFromCenter, segmentDirection * alongLineOffset);

			if (distanceToSegment < nearestDistance)
			{
				nearestDistance = distanceToSegment;
				nearestSegment = segment;
			}
		}

		return nearestSegment;
	}

	// MapLayerCollisionRelay points each segment box's forward axis along the painted line, with its length on the z size.
	private static void GetSegmentFrame(BoxCollider segment, out Vector3 center, out Vector3 alongLine, out float halfLength)
	{
		Transform segmentTransform = segment.transform;
		center = segmentTransform.TransformPoint(segment.center);
		alongLine = Vector3.ProjectOnPlane(segmentTransform.forward, Vector3.up).normalized;
		halfLength = Mathf.Abs(segment.size.z * segmentTransform.lossyScale.z) * 0.5f;
	}

	// 정지선 통과는 차체 기준으로 판정한다. 타이어 접지 센서(T자 검지선 전용)는 바퀴마다 따로 통과로 잡히므로 무시한다
	private void HandleSegmentEntered(Collider other)
	{
		if (TireContactSensor.IsTireSensor(other))
		{
			return;
		}

		GameObject crossingObject = GetCrossingObject(other);
		overlapCounts.TryGetValue(crossingObject, out int overlapCount);
		overlapCounts[crossingObject] = overlapCount + 1;

		bool isFirstContact = overlapCount == 0;
		if (!isFirstContact || trafficLight == null || IsReversingOverLine(other))
		{
			return;
		}

		Crossed?.Invoke(this, crossingObject);
	}

	private void HandleSegmentExited(Collider other)
	{
		if (TireContactSensor.IsTireSensor(other))
		{
			return;
		}

		GameObject crossingObject = GetCrossingObject(other);
		if (!overlapCounts.TryGetValue(crossingObject, out int overlapCount))
		{
			return;
		}

		if (overlapCount <= 1)
		{
			overlapCounts.Remove(crossingObject);
		}
		else
		{
			overlapCounts[crossingObject] = overlapCount - 1;
		}
	}

	// Backing over the line from the intersection side is not entering the intersection.
	private bool IsReversingOverLine(Collider other)
	{
		Rigidbody crossingBody = other.attachedRigidbody;
		return crossingBody != null && Vector3.Dot(crossingBody.linearVelocity, travelDirection) < 0f;
	}

	// Same owner rule as MapGenerator.NotifyLayerTrigger, so DrivingTestLevel.IsTestVehicle accepts it.
	private static GameObject GetCrossingObject(Collider other)
	{
		return other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireSphere(stopLineApproachPoint, ApproachPointGizmoRadius);

		if (boundSegment == null)
		{
			return;
		}

		Gizmos.color = Color.red;
		Gizmos.DrawLine(lineCenter - alongLineDirection * halfLineLength, lineCenter + alongLineDirection * halfLineLength);
		Gizmos.DrawRay(lineCenter, travelDirection * TravelDirectionGizmoLength);
		Gizmos.DrawLine(lineCenter, lineCenter - travelDirection * approachDistanceMeters);
	}
}
