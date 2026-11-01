using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using UnityEngine;

public class MapHillProfileTests
{
	[TestCase(0f)]
	[TestCase(30f)]
	[TestCase(90f)]
	[TestCase(-20f)]
	public void Build_RotatedRoad_RampsAndPlateauFollowRoad(float angle)
	{
		MapHillProfile profile = Build(angle);

		Assert.That(profile.IsValid, Is.True);
		Assert.That(profile.Stations.Count, Is.EqualTo(4));
		Assert.That(profile.GetHeight(Point(angle, 0f, 0f)), Is.EqualTo(0f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(angle, 5f, 0f)), Is.EqualTo(0.75f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(angle, 15f, 0f)), Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(angle, 25f, 0f)), Is.EqualTo(0.75f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(angle, 30f, 0f)), Is.EqualTo(0f).Within(0.001f));
	}

	[Test]
	public void GetHeight_OutsideRotatedRoadWidth_RemainsFlat()
	{
		MapHillProfile profile = Build(30f);

		Assert.That(profile.GetHeight(Point(30f, 15f, 8f)), Is.Zero);
		Assert.That(profile.GetHeight(Point(30f, -5f, 0f)), Is.Zero);
		Assert.That(profile.GetHeight(Point(30f, 35f, 0f)), Is.Zero);
	}

	[Test]
	public void GetHeight_CurbBesideRoad_UsesExplicitPaddingOnly()
	{
		MapHillProfile profile = Build(30f);
		Vector2 curbPoint = Point(30f, 15f, 4.2f);

		Assert.That(profile.GetHeight(curbPoint), Is.Zero);
		Assert.That(profile.GetHeight(curbPoint, 400f), Is.EqualTo(1.5f).Within(0.001f));
	}

	[Test]
	public void GetSegmentCuts_LongLine_CutsAtBothRampsAndPlateauEdges()
	{
		MapHillProfile profile = Build(30f);
		var cuts = new List<float>();

		profile.GetSegmentCuts(Point(30f, -10f, 0f), Point(30f, 40f, 0f), 0f, cuts);

		Assert.That(cuts.Count, Is.EqualTo(6));
		float[] expected = { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
		for (int i = 0; i < expected.Length; i++)
		{
			Assert.That(cuts[i], Is.EqualTo(expected[i]).Within(0.0001f));
		}
	}

	[Test]
	public void Build_SurfaceCorners_HeightAgreesAtBothRoadEdges()
	{
		MapHillProfile profile = Build(-20f);

		for (int i = 0; i < profile.Stations.Count; i++)
		{
			foreach (float lateral in new[] { profile.MinAcross, profile.MaxAcross })
			{
				Assert.That(profile.GetHeight(profile.ToCadPoint(profile.Stations[i], lateral)),
					Is.EqualTo(profile.GetStationHeight(i)).Within(0.001f));
			}
		}
	}

	[Test]
	public void Build_EmptyData_ClearsOldProfile()
	{
		MapHillProfile profile = Build(0f);

		profile.Build(new List<MapData>(), 0.001f, 1.5f);

		Assert.That(profile.IsValid, Is.False);
		Assert.That(profile.GetHeight(Point(0f, 15f, 0f)), Is.Zero);
	}

	[Test]
	public void Build_TwoStations_PreservesLegacyOneSidedRamp()
	{
		MapHillProfile profile = Build(30f, false);

		Assert.That(profile.Stations.Count, Is.EqualTo(2));
		Assert.That(profile.GetHeight(Point(30f, 15f, 0f)), Is.EqualTo(0.75f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(30f, 30f, 0f)), Is.EqualTo(1.5f).Within(0.001f));
	}

	[Test]
	public void GetSegmentHeights_OutsideSplitAtRaisedSide_DoesNotRampAcrossFlatGround()
	{
		MapHillProfile profile = Build(30f);

		profile.GetSegmentHeights(Point(30f, 15f, 8f), Point(30f, 15f, 4f), 0f,
			out float outsideStart, out float outsideEnd);
		profile.GetSegmentHeights(Point(30f, 15f, 4f), Point(30f, 15f, 0f), 0f,
			out float insideStart, out float insideEnd);

		Assert.That(outsideStart, Is.Zero);
		Assert.That(outsideEnd, Is.Zero);
		Assert.That(insideStart, Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(insideEnd, Is.EqualTo(1.5f).Within(0.001f));
	}

	[TestCase(30f)]
	[TestCase(-20f)]
	[TestCase(90f)]
	public void GetSegmentHeights_OnExactRoadEdge_DoesNotDropBelowSurface(float angle)
	{
		MapHillProfile profile = Build(angle);
		foreach (float lateral in new[] { profile.MinAcross, profile.MaxAcross })
		{
			Vector2 start = profile.ToCadPoint(profile.Stations[1], lateral);
			Vector2 end = profile.ToCadPoint(profile.Stations[2], lateral);
			profile.GetSegmentHeights(start, end, 0f, out float startHeight, out float endHeight);
			Assert.That(startHeight, Is.EqualTo(1.5f).Within(0.001f));
			Assert.That(endHeight, Is.EqualTo(1.5f).Within(0.001f));
		}
	}

	[TestCase(0f)]
	[TestCase(-20f)]
	[TestCase(30f)]
	public void ApplyCourseDimensions_RightAscentLeftDescent_HasFlatFourMeterSummit(float angle)
	{
		var csv = new StringBuilder("name,layer,start_x,start_y,end_x,end_y\n");
		AppendLine(csv, Point(angle, 0f, -4f), Point(angle, 32f, -4f));
		AppendLine(csv, Point(angle, 0f, 4f), Point(angle, 32f, 4f));
		AppendLine(csv, Point(angle, 16f, -4f), Point(angle, 16f, 4f));
		AppendLine(csv, Point(angle, 20f, -4f), Point(angle, 20f, 4f));
		var profile = new MapHillProfile();
		profile.Build(CsvParaser.GetMapDataFromText(csv.ToString()), 0.001f, 1.5f);

		profile.ApplyCourseDimensions(0.001f);

		Assert.That(profile.Stations.Count, Is.EqualTo(4));
		Assert.That((profile.Stations[2] - profile.Stations[1]) * 0.001f, Is.EqualTo(4f).Within(0.001f));
		float ascentLength = (profile.Stations[3] - profile.Stations[2]) * 0.001f;
		float descentLength = (profile.Stations[1] - profile.Stations[0]) * 0.001f;
		Assert.That(ascentLength, Is.EqualTo(12f).Within(0.001f));
		Assert.That(descentLength, Is.EqualTo(16.666666f).Within(0.001f));
		Assert.That(1.5f / ascentLength, Is.InRange(0.10f - 0.00001f, 0.125f + 0.00001f));
		Assert.That(1.5f / descentLength, Is.InRange(0.065f - 0.00001f, 0.09f + 0.00001f));
		Assert.That(profile.GetHeight(Point(angle, 17f, 0f)), Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(angle, 19f, 0f)), Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(angle, 26f, 0f)), Is.EqualTo(0.75f).Within(0.001f));
		Assert.That(profile.GetHeight(Point(angle, 7.666666f, 0f)), Is.EqualTo(0.75f).Within(0.001f));
	}

	[Test]
	public void ApplyCourseDimensions_TwoStationHill_CreatesLevelSummit()
	{
		MapHillProfile profile = Build(0f, false);

		profile.ApplyCourseDimensions(0.001f);

		Assert.That(profile.Stations.Count, Is.EqualTo(4));
		Assert.That(profile.GetHeight(Point(0f, 15f, 0f)), Is.EqualTo(1.5f).Within(0.001f));
	}

	[Test]
	public void ApplyCourseDimensions_OffCenterThreeStationPeak_PreservesPeakCenter()
	{
		var csv = new StringBuilder("name,layer,start_x,start_y,end_x,end_y\n");
		AppendLine(csv, Point(0f, 0f, -4f), Point(0f, 32f, -4f));
		AppendLine(csv, Point(0f, 0f, 4f), Point(0f, 32f, 4f));
		AppendLine(csv, Point(0f, 12f, -4f), Point(0f, 12f, 4f));
		var profile = new MapHillProfile();
		profile.Build(CsvParaser.GetMapDataFromText(csv.ToString()), 0.001f, 1.5f);

		profile.ApplyCourseDimensions(0.001f);

		Assert.That(profile.ToCadPoint((profile.Stations[1] + profile.Stations[2]) * 0.5f, profile.MinAcross).x,
			Is.EqualTo(Point(0f, 12f, -4f).x).Within(0.001f));
	}

	[Test]
	public void ApplyCourseDimensions_OverlyGentleRamps_ShortensToMinimumGrades()
	{
		var csv = new StringBuilder("name,layer,start_x,start_y,end_x,end_y\n");
		AppendLine(csv, Point(0f, 0f, -4f), Point(0f, 100f, -4f));
		AppendLine(csv, Point(0f, 0f, 4f), Point(0f, 100f, 4f));
		AppendLine(csv, Point(0f, 40f, -4f), Point(0f, 40f, 4f));
		AppendLine(csv, Point(0f, 44f, -4f), Point(0f, 44f, 4f));
		var profile = new MapHillProfile();
		profile.Build(CsvParaser.GetMapDataFromText(csv.ToString()), 0.001f, 1.5f);

		profile.ApplyCourseDimensions(0.001f);

		Assert.That(1.5f / ((profile.Stations[3] - profile.Stations[2]) * 0.001f), Is.EqualTo(0.10f).Within(0.00001f));
		Assert.That(1.5f / ((profile.Stations[1] - profile.Stations[0]) * 0.001f), Is.EqualTo(0.065f).Within(0.00001f));
		Assert.That(profile.GetHeight(Point(0f, 5f, 0f)), Is.Zero);
		Assert.That(profile.GetHeight(Point(0f, 80f, 0f)), Is.Zero);
	}

	private static MapHillProfile Build(float angle, bool includeCrossLines = true)
	{
		var csv = new StringBuilder("name,layer,start_x,start_y,end_x,end_y\n");
		AppendLine(csv, Point(angle, 0f, -4f), Point(angle, 30f, -4f));
		AppendLine(csv, Point(angle, 0f, 4f), Point(angle, 30f, 4f));
		if (includeCrossLines)
		{
			AppendLine(csv, Point(angle, 10f, -4f), Point(angle, 10f, 4f));
			AppendLine(csv, Point(angle, 20f, -4f), Point(angle, 20f, 4f));
		}
		var profile = new MapHillProfile();
		profile.Build(CsvParaser.GetMapDataFromText(csv.ToString()), 0.001f, 1.5f);
		return profile;
	}

	private static Vector2 Point(float angle, float alongMeters, float acrossMeters)
	{
		float radians = angle * Mathf.Deg2Rad;
		Vector2 forward = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
		Vector2 across = new Vector2(-forward.y, forward.x);
		return new Vector2(10000f, 20000f) + forward * (alongMeters * 1000f) + across * (acrossMeters * 1000f);
	}

	private static void AppendLine(StringBuilder csv, Vector2 start, Vector2 end)
	{
		csv.AppendFormat(CultureInfo.InvariantCulture, "Line,Hill,{0:R},{1:R},{2:R},{3:R}\n", start.x, start.y, end.x, end.y);
	}
}
