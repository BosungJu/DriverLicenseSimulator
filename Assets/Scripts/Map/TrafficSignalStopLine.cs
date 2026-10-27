using System;
using System.Collections.Generic;
using UnityEngine;

// Stop line of one approach lane at a signalized intersection.
// Its forward axis points in the travel direction (toward the intersection); the box trigger spans the lane at the line.
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class TrafficSignalStopLine : MonoBehaviour
{
	// Default trigger: one lane wide, tall enough to reach the vehicle body, thin along the travel direction (meters).
	private static readonly Vector3 DefaultTriggerSize = new Vector3(3.5f, 2f, 0.3f);

	[SerializeField] private TrafficLight trafficLight;
	[Tooltip("A vehicle within this distance before the line is waiting at this light, in meters.")]
	[SerializeField, Min(0f)] private float approachDistanceMeters = 30f;

	// Overlapping collider count per vehicle, so the body and wheel colliders of one vehicle cross once.
	private readonly Dictionary<GameObject, int> overlapCounts = new Dictionary<GameObject, int>();
	private BoxCollider stopLineTrigger;

	public TrafficLight TrafficLight => trafficLight;
	public TrafficLightState SignalState => trafficLight.CurrentState;

	// Raised when a vehicle's front first reaches the line while moving toward the intersection.
	public event Action<TrafficSignalStopLine, GameObject> Crossed;

	private void Reset()
	{
		var trigger = GetComponent<BoxCollider>();
		trigger.isTrigger = true;
		trigger.size = DefaultTriggerSize;
		trigger.center = new Vector3(0f, DefaultTriggerSize.y * 0.5f, 0f);
	}

	private void Awake()
	{
		stopLineTrigger = GetComponent<BoxCollider>();
		stopLineTrigger.isTrigger = true;

		if (trafficLight == null)
		{
			Debug.LogWarning($"[TrafficSignalStopLine] {name}: TrafficLight is not assigned, so crossings are not reported.", this);
		}
	}

	// Exits are not reported while disabled, so stale overlaps must not survive re-enabling.
	private void OnDisable()
	{
		overlapCounts.Clear();
	}

	private void OnTriggerEnter(Collider other)
	{
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

	private void OnTriggerExit(Collider other)
	{
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

	// True when the position is in this lane, before the line and within the approach distance.
	public bool IsApproachedFrom(Vector3 worldPosition)
	{
		if (trafficLight == null || stopLineTrigger == null)
		{
			return false;
		}

		Vector3 lineCenter = transform.TransformPoint(stopLineTrigger.center);
		Vector3 offsetFromLine = worldPosition - lineCenter;
		float distanceBeforeLine = -Vector3.Dot(offsetFromLine, transform.forward);
		float lateralOffset = Mathf.Abs(Vector3.Dot(offsetFromLine, transform.right));
		float halfLaneWidth = Mathf.Abs(stopLineTrigger.size.x * transform.lossyScale.x) * 0.5f;

		return distanceBeforeLine > 0f && distanceBeforeLine <= approachDistanceMeters && lateralOffset <= halfLaneWidth;
	}

	// Backing over the line from the intersection side is not entering the intersection.
	private bool IsReversingOverLine(Collider other)
	{
		Rigidbody crossingBody = other.attachedRigidbody;
		return crossingBody != null && Vector3.Dot(crossingBody.linearVelocity, transform.forward) < 0f;
	}

	// Same owner rule as MapGenerator.NotifyLayerTrigger, so DrivingTestLevel.IsTestVehicle accepts it.
	private static GameObject GetCrossingObject(Collider other)
	{
		return other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.red;
		Vector3 lineCenter = transform.position;
		Gizmos.DrawLine(lineCenter, lineCenter - transform.forward * approachDistanceMeters);
		Gizmos.DrawRay(lineCenter, transform.forward * 2f);
	}
}
