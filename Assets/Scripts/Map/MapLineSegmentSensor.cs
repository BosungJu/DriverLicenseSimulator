using System;
using UnityEngine;

// Reports trigger contacts of one generated line segment box.
// MapLayerCollisionRelay receives the contacts of every segment through its shared kinematic body,
// so a listener that must tell segments of the same layer apart (e.g. one stop line) listens here instead.
[DisallowMultipleComponent]
public class MapLineSegmentSensor : MonoBehaviour
{
	public event Action<Collider> TriggerEntered;
	public event Action<Collider> TriggerExited;

	private void OnTriggerEnter(Collider other)
	{
		TriggerEntered?.Invoke(other);
	}

	private void OnTriggerExit(Collider other)
	{
		TriggerExited?.Invoke(other);
	}
}
