using System.Collections.Generic;
using UnityEngine;

// Describes one CAD hill in its own longitudinal/across-road coordinate frame.
public sealed class MapHillProfile
{
	private const float StationMergeDistanceMeters = 0.05f;
	private const float CourseSummitLengthMeters = 4f;
	private const float MinimumAscentGrade = 0.10f;
	private const float MaximumAscentGrade = 0.125f;
	private const float MinimumDescentGrade = 0.065f;
	private const float MaximumDescentGrade = 0.09f;
	private readonly List<float> stations = new List<float>();
	private Vector2 origin;
	private Vector2 forward;
	private Vector2 across;
	private float height;
	private float boundaryTolerance;

	public bool IsValid { get; private set; }
	public IReadOnlyList<float> Stations => stations;
	public float MinAcross { get; private set; }
	public float MaxAcross { get; private set; }

	public void Clear()
	{
		IsValid = false;
		stations.Clear();
	}

	public void Build(IReadOnlyList<MapData> hillLines, float cadUnitScale, float hillHeight)
	{
		Clear();
		if (hillLines == null || hillLines.Count < 2 || cadUnitScale <= 0f)
		{
			return;
		}

		float longestLengthSquared = 0f;
		foreach (MapData line in hillLines)
		{
			Vector2 start = Start(line);
			Vector2 direction = End(line) - start;
			if (direction.sqrMagnitude > longestLengthSquared)
			{
				longestLengthSquared = direction.sqrMagnitude;
				origin = start;
				forward = direction.normalized;
			}
		}
		if (longestLengthSquared <= Mathf.Epsilon)
		{
			return;
		}

		// A consistent direction makes station ordering independent of the entity's endpoint order.
		if (forward.x < 0f || (forward.x == 0f && forward.y < 0f))
		{
			forward = -forward;
		}
		across = new Vector2(-forward.y, forward.x);
		height = hillHeight;
		boundaryTolerance = 0.001f / cadUnitScale;
		MinAcross = float.PositiveInfinity;
		MaxAcross = float.NegativeInfinity;
		var distances = new List<float>();
		foreach (MapData line in hillLines)
		{
			AddEndpoint(Start(line), distances);
			AddEndpoint(End(line), distances);
		}

		distances.Sort();
		float mergeTolerance = StationMergeDistanceMeters / cadUnitScale;
		foreach (float distance in distances)
		{
			if (stations.Count == 0 || distance - stations[stations.Count - 1] > mergeTolerance)
			{
				stations.Add(distance);
			}
		}
		if (stations.Count < 2 || MaxAcross - MinAcross <= Mathf.Epsilon)
		{
			return;
		}
		stations[stations.Count - 1] = distances[distances.Count - 1];
		IsValid = true;
	}

	public void ApplyCourseDimensions(float cadUnitScale)
	{
		if (!IsValid || cadUnitScale <= 0f || height <= 0f)
		{
			return;
		}

		// CAD +X is the right side: approach from the right, exit downhill to the left.
		// Preserve the existing summit center and adjust only the ramp feet as needed.
		float summitCenter = stations.Count >= 3
			? (stations[1] + stations[stations.Count - 2]) * 0.5f
			: (stations[0] + stations[stations.Count - 1]) * 0.5f;
		float halfSummitLength = CourseSummitLengthMeters * 0.5f / cadUnitScale;
		float summitLeft = summitCenter - halfSummitLength;
		float summitRight = summitCenter + halfSummitLength;
		float descentLength = Mathf.Clamp(summitLeft - stations[0],
			height / MaximumDescentGrade / cadUnitScale, height / MinimumDescentGrade / cadUnitScale);
		float ascentLength = Mathf.Clamp(stations[stations.Count - 1] - summitRight,
			height / MaximumAscentGrade / cadUnitScale, height / MinimumAscentGrade / cadUnitScale);

		stations.Clear();
		stations.Add(summitLeft - descentLength);
		stations.Add(summitLeft);
		stations.Add(summitRight);
		stations.Add(summitRight + ascentLength);
	}

	private void AddEndpoint(Vector2 point, List<float> distances)
	{
		Vector2 offset = point - origin;
		distances.Add(Vector2.Dot(offset, forward));
		float lateral = Vector2.Dot(offset, across);
		MinAcross = Mathf.Min(MinAcross, lateral);
		MaxAcross = Mathf.Max(MaxAcross, lateral);
	}

	public Vector2 ToCadPoint(float distance, float lateral)
	{
		return origin + forward * distance + across * lateral;
	}

	public float GetStationHeight(int index)
	{
		// Preserve the legacy two-station, one-sided ramp contract.
		if (stations.Count == 2)
		{
			return index == 0 ? 0f : height;
		}
		return index == 0 || index == stations.Count - 1 ? 0f : height;
	}

	public float GetHeight(Vector2 cadPoint, float acrossPadding = 0f)
	{
		if (!IsValid)
		{
			return 0f;
		}
		Vector2 offset = cadPoint - origin;
		float lateral = Vector2.Dot(offset, across);
		float distance = Vector2.Dot(offset, forward);
		if (lateral < MinAcross - acrossPadding - boundaryTolerance || lateral > MaxAcross + acrossPadding + boundaryTolerance
			|| distance < stations[0] - boundaryTolerance || distance > stations[stations.Count - 1] + boundaryTolerance)
		{
			return 0f;
		}
		distance = Mathf.Clamp(distance, stations[0], stations[stations.Count - 1]);
		for (int i = 0; i < stations.Count - 1; i++)
		{
			if (distance <= stations[i + 1])
			{
				float t = Mathf.InverseLerp(stations[i], stations[i + 1], distance);
				return Mathf.Lerp(GetStationHeight(i), GetStationHeight(i + 1), t);
			}
		}
		return 0f;
	}

	public void GetSegmentHeights(Vector2 start, Vector2 end, float acrossPadding, out float startHeight, out float endHeight)
	{
		Vector2 midpoint = (start + end) * 0.5f;
		Vector2 offset = midpoint - origin;
		float distance = Vector2.Dot(offset, forward);
		float lateral = Vector2.Dot(offset, across);
		if (!IsValid || distance < stations[0] - boundaryTolerance || distance > stations[stations.Count - 1] + boundaryTolerance
			|| lateral < MinAcross - acrossPadding - boundaryTolerance || lateral > MaxAcross + acrossPadding + boundaryTolerance)
		{
			// A split outside the footprint stays on the ground, even when its endpoint touches the raised side.
			startHeight = 0f;
			endHeight = 0f;
			return;
		}
		startHeight = GetHeight(start, acrossPadding);
		endHeight = GetHeight(end, acrossPadding);
	}

	public void GetSegmentCuts(Vector2 start, Vector2 end, float acrossPadding, List<float> cuts)
	{
		cuts.Clear();
		cuts.Add(0f);
		cuts.Add(1f);
		if (!IsValid)
		{
			return;
		}
		float startAcross = Vector2.Dot(start - origin, across);
		float endAcross = Vector2.Dot(end - origin, across);
		if (Mathf.Max(startAcross, endAcross) < MinAcross - acrossPadding
			|| Mathf.Min(startAcross, endAcross) > MaxAcross + acrossPadding)
		{
			return;
		}
		float startDistance = Vector2.Dot(start - origin, forward);
		float endDistance = Vector2.Dot(end - origin, forward);
		foreach (float station in stations)
		{
			AddCut(startDistance, endDistance, station, cuts);
		}
		AddCut(startAcross, endAcross, MinAcross - acrossPadding, cuts);
		AddCut(startAcross, endAcross, MaxAcross + acrossPadding, cuts);
		cuts.Sort();
	}

	private void AddCut(float start, float end, float boundary, List<float> cuts)
	{
		if (Mathf.Abs(end - start) <= Mathf.Epsilon)
		{
			return;
		}
		float t = (boundary - start) / (end - start);
		float tolerance = boundaryTolerance / Mathf.Abs(end - start);
		if (t <= tolerance || t >= 1f - tolerance)
		{
			return;
		}
		foreach (float cut in cuts)
		{
			if (Mathf.Abs(t - cut) <= tolerance)
			{
				return;
			}
		}
		cuts.Add(t);
	}

	private static Vector2 Start(MapData line)
	{
		Vector2 point = new Vector2(line.StartX, line.StartY);
		return point != Vector2.zero ? point : new Vector2(line.PosX, line.PosY);
	}

	private static Vector2 End(MapData line)
	{
		Vector2 point = new Vector2(line.EndX, line.EndY);
		return point != Vector2.zero ? point : new Vector2(line.PosX + line.DeltaX, line.PosY + line.DeltaY);
	}
}
