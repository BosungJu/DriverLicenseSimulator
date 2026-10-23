using System.Collections.Generic;
using UnityEngine;

public class MapLayerCollisionRelay : MonoBehaviour
{
    // MapGenerator.DrawLine writes each segment as start-left, start-right, end-left, end-right.
    private const int VerticesPerLineSegment = 4;
    // Tall enough to reach the vehicle body collider above the wheels, measured from the painted line in meters.
    private const float LineTriggerHeight = 1f;
    // Covers the small gap between the painted line and the ground below it, in meters.
    private const float LineTriggerDepthBelowLine = 0.1f;

    [SerializeField] private MapGenerator mapGenerator;
    [SerializeField] private string layerName;
    [SerializeField] private int routePointIndex = -1;

    // Overlapping segment trigger count per object, so one continuous contact across connected segments notifies once.
    private readonly Dictionary<GameObject, int> lineOverlapCounts = new Dictionary<GameObject, int>();
    private bool hasLineTriggers;

    private void Awake()
    {
        ReplaceMeshColliderWithLineTriggers();
    }

    // Exits are not reported while this relay is disabled, so stale overlaps must not survive re-enabling.
    private void OnDisable()
    {
        lineOverlapCounts.Clear();
    }

    public void Initialize(MapGenerator generator, string targetLayerName)
    {
        Initialize(generator, targetLayerName, -1);
    }

    public void Initialize(MapGenerator generator, string targetLayerName, int targetRoutePointIndex)
    {
        mapGenerator = generator;
        layerName = targetLayerName;
        routePointIndex = targetRoutePointIndex;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (mapGenerator == null)
        {
            return;
        }

        mapGenerator.NotifyLayerCollision(layerName, collision);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (mapGenerator == null)
        {
            return;
        }

        if (hasLineTriggers && !BeginLineOverlap(other))
        {
            return;
        }

        mapGenerator.NotifyLayerTrigger(layerName, routePointIndex, other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (hasLineTriggers)
        {
            EndLineOverlap(other);
        }
    }

    // Returns true only when the object starts touching this layer.
    private bool BeginLineOverlap(Collider other)
    {
        GameObject overlappingObject = GetOverlappingObject(other);
        lineOverlapCounts.TryGetValue(overlappingObject, out int overlapCount);
        lineOverlapCounts[overlappingObject] = overlapCount + 1;
        return overlapCount == 0;
    }

    private void EndLineOverlap(Collider other)
    {
        GameObject overlappingObject = GetOverlappingObject(other);
        if (!lineOverlapCounts.TryGetValue(overlappingObject, out int overlapCount))
        {
            return;
        }

        if (overlapCount <= 1)
        {
            lineOverlapCounts.Remove(overlappingObject);
        }
        else
        {
            lineOverlapCounts[overlappingObject] = overlapCount - 1;
        }
    }

    // Same owner rule as MapGenerator.NotifyLayerTrigger, so the body and wheel colliders of one vehicle count together.
    private static GameObject GetOverlappingObject(Collider other)
    {
        return other.attachedRigidbody != null ? other.attachedRigidbody.gameObject : other.gameObject;
    }

    // Painted lines must not lift the wheels, but a non-convex MeshCollider cannot be a trigger.
    // In Play mode each painted segment gets its own trigger box; the edit-mode mesh collider stays for the generator.
    private void ReplaceMeshColliderWithLineTriggers()
    {
        if (!TryGetComponent(out MeshCollider lineMeshCollider)
            || !TryGetComponent(out MeshFilter lineMeshFilter)
            || lineMeshFilter.sharedMesh == null)
        {
            return;
        }

        Vector3[] lineVertices = lineMeshFilter.sharedMesh.vertices;
        if (lineVertices.Length % VerticesPerLineSegment != 0)
        {
            Debug.LogWarning($"[MapLayerCollisionRelay] {name}: mesh is not made of line segments, so it keeps its solid collider.", this);
            return;
        }

        lineMeshCollider.enabled = false;

        // Trigger messages from the child boxes reach this relay through the kinematic body.
        if (!TryGetComponent(out Rigidbody triggerBody))
        {
            triggerBody = gameObject.AddComponent<Rigidbody>();
        }

        triggerBody.isKinematic = true;
        triggerBody.useGravity = false;
        hasLineTriggers = true;

        for (int i = 0; i < lineVertices.Length; i += VerticesPerLineSegment)
        {
            CreateSegmentTrigger(i / VerticesPerLineSegment,
                lineVertices[i], lineVertices[i + 1], lineVertices[i + 2], lineVertices[i + 3]);
        }
    }

    // Vertices are in this object's local space, so the trigger boxes are created as children with local poses.
    private void CreateSegmentTrigger(int segmentIndex, Vector3 startLeft, Vector3 startRight, Vector3 endLeft, Vector3 endRight)
    {
        Vector3 segmentStart = (startLeft + startRight) * 0.5f;
        Vector3 segmentEnd = (endLeft + endRight) * 0.5f;
        Vector3 segmentDirection = segmentEnd - segmentStart;
        float segmentLength = segmentDirection.magnitude;
        if (segmentLength <= Mathf.Epsilon)
        {
            return;
        }

        float lineWidth = Vector3.Distance(startLeft, startRight);

        var segmentObject = new GameObject($"Line Trigger {segmentIndex}");
        segmentObject.layer = gameObject.layer;
        segmentObject.transform.SetParent(transform, false);
        segmentObject.transform.localPosition = (segmentStart + segmentEnd) * 0.5f;
        segmentObject.transform.localRotation = Quaternion.LookRotation(segmentDirection, Vector3.up);

        var segmentTrigger = segmentObject.AddComponent<BoxCollider>();
        segmentTrigger.isTrigger = true;
        segmentTrigger.size = new Vector3(lineWidth, LineTriggerHeight + LineTriggerDepthBelowLine, segmentLength);
        segmentTrigger.center = new Vector3(0f, (LineTriggerHeight - LineTriggerDepthBelowLine) * 0.5f, 0f);
    }
}
