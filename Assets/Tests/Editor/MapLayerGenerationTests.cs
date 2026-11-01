using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class MapLayerGenerationTests
{
	private GameObject mapObject;
	private MapGenerator generator;

	#region Setup and Teardown

	[SetUp]
	public void SetUp()
	{
		mapObject = new GameObject("Layer generation test");
		generator = mapObject.AddComponent<MapGenerator>();
		generator.enabled = false;
		var settings = new SerializedObject(generator);
		settings.FindProperty("importHillFromLegacyCsv").boolValue = false;
		settings.FindProperty("useTestCourseHillProfile").boolValue = false;
		settings.ApplyModifiedPropertiesWithoutUndo();
	}

	[TearDown]
	public void TearDown()
	{
		generator.ClearMap();
		Object.DestroyImmediate(mapObject);
	}

	private void GenerateLine(string layer)
	{
		generator.SetMapData(CsvParaser.GetMapDataFromText(
			"name,layer,start_x,start_y,end_x,end_y\nLine," + layer + ",1000,1000,11000,1000"));
	}

	#endregion

	#region Layer Behavior

	[TestCase("Line")]
	[TestCase("line")]
	[TestCase("Outline")]
	public void SetMapData_MarkingLayer_CreatesFlatMeshWithoutCollider(string layer)
	{
		GenerateLine(layer);

		Transform child = mapObject.transform.Find("Generated " + (layer == "line" ? "Line" : layer) + " Layer");
		Assert.That(child, Is.Not.Null);
		Assert.That(child.GetComponent<MeshFilter>().sharedMesh.bounds.size.y, Is.EqualTo(0f).Within(0.0001f));
		Assert.That(child.GetComponentsInChildren<Collider>(), Is.Empty);
		if (layer == "Outline")
		{
			Assert.That(child.GetComponent<MeshRenderer>().sharedMaterial.color, Is.EqualTo(Color.yellow));
		}
	}

	[TestCase("Curb")]
	[TestCase("curb")]
	public void SetMapData_Curb_CreatesSolidRaisedBoundary(string layer)
	{
		GenerateLine(layer);

		Transform child = mapObject.transform.Find("Generated " + layer + " Layer");
		Mesh mesh = child.GetComponent<MeshFilter>().sharedMesh;
		MeshCollider collider = child.GetComponent<MeshCollider>();
		Assert.That(mesh.bounds.size.y, Is.EqualTo(0.15f).Within(0.0001f));
		Assert.That(collider.sharedMesh, Is.SameAs(mesh));
		Assert.That(collider.enabled, Is.True);
		Assert.That(collider.isTrigger, Is.False);
		Physics.SyncTransforms();
		Assert.That(collider.Raycast(new Ray(new Vector3(mesh.bounds.center.x, mesh.bounds.center.y, mesh.bounds.min.z - 1f), Vector3.forward), out _, 3f), Is.True);
	}

	[Test]
	public void SetMapData_Curb_UsesMetersAlongLengthAndLocalHeightForBlockPattern()
	{
		GenerateLine("Curb");

		Mesh mesh = mapObject.transform.Find("Generated Curb Layer").GetComponent<MeshFilter>().sharedMesh;
		Vector2[] coordinates = mesh.uv;
		Assert.That(coordinates.Length, Is.EqualTo(mesh.vertexCount));
		// The side spans a 10m line plus the two existing corner overlaps.
		float width = mesh.bounds.size.z;
		Assert.That(coordinates[9].x - coordinates[8].x, Is.EqualTo(10f + width).Within(0.001f));
		Assert.That(coordinates[8].y, Is.EqualTo(0f).Within(0.001f));
		Assert.That(coordinates[10].y, Is.EqualTo(0.15f).Within(0.001f));
	}

	[Test]
	public void SetMapData_Regenerate_ReplacesOldCurbAndKeepsMarkingsNonColliding()
	{
		GenerateLine("Curb");

		GenerateLine("Outline");
		GenerateLine("Line");

		Assert.That(mapObject.transform.Find("Generated Curb Layer"), Is.Null);
		Assert.That(mapObject.transform.Find("Generated Outline Layer"), Is.Null);
		Assert.That(mapObject.transform.Find("Generated Line Layer").GetComponentsInChildren<Collider>(), Is.Empty);
	}

	[Test]
	public void SetMapData_TestLayer_PreservesSensorMesh()
	{
		GenerateLine("T_Test");

		Assert.That(mapObject.transform.Find("Generated T_Test Layer").GetComponent<MeshCollider>(), Is.Not.Null);
	}

	[Test]
	public void SetMapData_RotatedHill_SurfaceAndMarkingsFollowRoadSlope()
	{
		generator.SetMapData(CsvParaser.GetMapDataFromText(
			"name,layer,start_x,start_y,end_x,end_y\n"
			+ "Line,Hill,1000,1000,31000,31000\n"
			+ "Line,Hill,5000,-3000,35000,27000\n"
			+ "Line,Hill,11000,11000,15000,7000\n"
			+ "Line,Hill,21000,21000,25000,17000\n"
			+ "Line,Line,3000,-1000,33000,29000"));

		Mesh surface = mapObject.transform.Find("Generated Hill Surface").GetComponent<MeshFilter>().sharedMesh;
		Vector3[] points = surface.vertices;
		Assert.That(points.Length, Is.GreaterThan(8));
		Vector3 across = (points[1] - points[0]).normalized;
		Assert.That(Vector3.Dot(across, new Vector3(1f, 0f, 1f).normalized), Is.EqualTo(0f).Within(0.001f));
		Assert.That(surface.bounds.max.y, Is.EqualTo(1.5f).Within(0.001f));
		MeshCollider collider = mapObject.transform.Find("Generated Hill Surface").GetComponent<MeshCollider>();
		Vector3 lowerPlateau = (points[2] + points[4]) * 0.5f;
		Vector3 upperPlateau = (points[3] + points[5]) * 0.5f;
		Physics.SyncTransforms();
		Assert.That(collider.Raycast(new Ray(lowerPlateau - across + Vector3.down * 0.75f, across), out RaycastHit lowerHit, 2f), Is.True);
		Assert.That(Vector3.Dot(lowerHit.normal, -across), Is.GreaterThan(0.99f));
		Assert.That(collider.Raycast(new Ray(upperPlateau + across + Vector3.down * 0.75f, -across), out RaycastHit upperHit, 2f), Is.True);
		Assert.That(Vector3.Dot(upperHit.normal, across), Is.GreaterThan(0.99f));
		Mesh marking = mapObject.transform.Find("Generated Line Layer").GetComponent<MeshFilter>().sharedMesh;
		Assert.That(marking.bounds.size.y, Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(marking.vertexCount, Is.EqualTo(12));
	}

	[Test]
	public void SetMapData_CurbBesideHill_RetainingSidesReachGround()
	{
		generator.SetMapData(CsvParaser.GetMapDataFromText(
			"name,layer,start_x,start_y,end_x,end_y\n"
			+ "Line,Hill,1000,1000,31000,1000\n"
			+ "Line,Hill,1000,9000,31000,9000\n"
			+ "Line,Hill,11000,1000,11000,9000\n"
			+ "Line,Hill,21000,1000,21000,9000\n"
			+ "Line,Curb,1000,800,31000,800"));

		Mesh curb = mapObject.transform.Find("Generated Curb Layer").GetComponent<MeshFilter>().sharedMesh;
		Assert.That(curb.bounds.min.y, Is.EqualTo(0f).Within(0.001f));
		Assert.That(curb.bounds.max.y, Is.GreaterThanOrEqualTo(1.65f - 0.001f));
		Assert.That(curb.vertexCount, Is.EqualTo(72));
	}

	[Test]
	public void SetMapData_Hill_SideWallsReachBasePlaneAndBlockFromBothSides()
	{
		generator.SetMapData(CsvParaser.GetMapDataFromText(
			"name,layer,start_x,start_y,end_x,end_y\n"
			+ "Line,Hill,1000,1000,31000,1000\n"
			+ "Line,Hill,1000,9000,31000,9000\n"
			+ "Line,Hill,11000,1000,11000,9000\n"
			+ "Line,Hill,21000,1000,21000,9000"));

		MeshCollider collider = mapObject.transform.Find("Generated Hill Surface").GetComponent<MeshCollider>();
		Mesh surface = collider.sharedMesh;
		float baseHeight = new SerializedObject(generator).FindProperty("planeHeight").floatValue;
		Assert.That(surface.bounds.min.y, Is.EqualTo(baseHeight).Within(0.001f));
		Assert.That(surface.bounds.max.y, Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(collider.isTrigger, Is.False);
		Physics.SyncTransforms();
		Assert.That(collider.Raycast(new Ray(new Vector3(surface.bounds.center.x, surface.bounds.max.y * 0.5f, surface.bounds.min.z - 1f), Vector3.forward), out RaycastHit lowerSide, 2f), Is.True);
		Assert.That(lowerSide.normal.z, Is.LessThan(-0.99f));
		Assert.That(collider.Raycast(new Ray(new Vector3(surface.bounds.center.x, surface.bounds.max.y * 0.5f, surface.bounds.max.z + 1f), Vector3.back), out RaycastHit upperSide, 2f), Is.True);
		Assert.That(upperSide.normal.z, Is.GreaterThan(0.99f));
		Assert.That(collider.Raycast(new Ray(new Vector3(surface.bounds.min.x - 1f, surface.bounds.max.y / 3f, surface.bounds.center.z), Vector3.right), out _, 2f), Is.False);

		GenerateLine("Line");

		Assert.That(mapObject.transform.Find("Generated Hill Surface"), Is.Null);
	}

	[Test]
	public void SetMapData_HillCurbs_AreGrayRaisedOutsideRoadAndRemovedOnRegeneration()
	{
		// Previously saved scenes may still contain the old thin-curb width.
		var legacySettings = new SerializedObject(generator);
		legacySettings.FindProperty("hillCurbWidth").floatValue = 0.1f;
		legacySettings.ApplyModifiedPropertiesWithoutUndo();
		generator.SetMapData(CsvParaser.GetMapDataFromText(
			"name,layer,start_x,start_y,end_x,end_y\n"
			+ "Line,Hill,1000,1000,31000,1000\n"
			+ "Line,Hill,1000,9000,31000,9000\n"
			+ "Line,Hill,11000,1000,11000,9000\n"
			+ "Line,Hill,21000,1000,21000,9000"));

		Transform curbs = mapObject.transform.Find("Generated Hill Curbs");
		Mesh mesh = curbs.GetComponent<MeshFilter>().sharedMesh;
		MeshCollider collider = curbs.GetComponent<MeshCollider>();
		Assert.That(curbs.GetComponent<MeshRenderer>().sharedMaterial.color, Is.EqualTo(Color.gray));
		Assert.That(mesh.bounds.max.y, Is.EqualTo(3f).Within(0.001f));
		Vector2[] coordinates = mesh.uv;
		Assert.That(coordinates.Length, Is.EqualTo(mesh.vertexCount));
		for (int segment = 0; segment < mesh.vertexCount; segment += 24)
		{
			// Upper side vertices remain 1.5m above the local road, even at the summit.
			Assert.That(coordinates[segment + 10].y, Is.EqualTo(1.5f).Within(0.001f));
			Assert.That(coordinates[segment + 11].y, Is.EqualTo(1.5f).Within(0.001f));
		}
		Assert.That(mesh.bounds.size.z, Is.EqualTo(8.8f).Within(0.001f));
		float baseHeight = new SerializedObject(generator).FindProperty("planeHeight").floatValue;
		Assert.That(mesh.bounds.min.y, Is.EqualTo(baseHeight).Within(0.001f));
		Assert.That(collider.isTrigger, Is.False);
		foreach (Vector3 point in mesh.vertices)
		{
			Assert.That(Mathf.Abs(point.z), Is.GreaterThanOrEqualTo(4f - 0.001f));
		}
		Physics.SyncTransforms();
		// Independent expected road heights at the toe, slope and plateau of this 30m fixture.
		float[] alongPositions = { -14f, -10f, 0f, 10f, 14f };
		float[] roadHeights = { 0.15f, 0.75f, 1.5f, 0.75f, 0.15f };
		for (int i = 0; i < alongPositions.Length; i++)
		{
			MeshCollider road = mapObject.transform.Find("Generated Hill Surface").GetComponent<MeshCollider>();
			Assert.That(road.Raycast(new Ray(new Vector3(alongPositions[i], 10f, 0f), Vector3.down), out RaycastHit roadHit, 20f), Is.True);
			Assert.That(roadHit.point.y, Is.EqualTo(roadHeights[i]).Within(0.001f));
			foreach (float clearance in new[] { 0.75f, 1.4f })
			{
				Vector3 bodyPosition = new Vector3(alongPositions[i], roadHit.point.y + clearance, 0f);
				Assert.That(collider.Raycast(new Ray(bodyPosition, Vector3.forward), out _, 5f), Is.True, $"Lower side at {alongPositions[i]}m, height {clearance}m");
				Assert.That(collider.Raycast(new Ray(bodyPosition, Vector3.back), out _, 5f), Is.True, $"Upper side at {alongPositions[i]}m, height {clearance}m");
			}
		}
		Assert.That(collider.Raycast(new Ray(new Vector3(mesh.bounds.min.x - 1f, 0.05f, 0f), Vector3.right), out _, 2f), Is.False);

		GenerateLine("Line");

		Assert.That(mapObject.transform.Find("Generated Hill Curbs"), Is.Null);
	}

	[Test]
	public void SetMapData_TestCourseProfile_ExtendsLeftRampAndKeepsFlatSummit()
	{
		var settings = new SerializedObject(generator);
		settings.FindProperty("useTestCourseHillProfile").boolValue = true;
		settings.ApplyModifiedPropertiesWithoutUndo();

		generator.SetMapData(CsvParaser.GetMapDataFromText(
			"name,layer,start_x,start_y,end_x,end_y\n"
			+ "Line,Hill,1000,1000,33000,1000\n"
			+ "Line,Hill,1000,9000,33000,9000\n"
			+ "Line,Hill,17000,1000,17000,9000\n"
			+ "Line,Hill,21000,1000,21000,9000"));

		Mesh surface = mapObject.transform.Find("Generated Hill Surface").GetComponent<MeshFilter>().sharedMesh;
		Vector3[] points = surface.vertices;
		Assert.That(points[0].x, Is.EqualTo(-16.666666f).Within(0.001f));
		Assert.That(points[6].x, Is.EqualTo(16f).Within(0.001f));
		Assert.That(points[4].x - points[2].x, Is.EqualTo(4f).Within(0.001f));
		Assert.That(points[2].y, Is.EqualTo(1.5f).Within(0.001f));
		Assert.That(points[4].y, Is.EqualTo(1.5f).Within(0.001f));
		Mesh barriers = mapObject.transform.Find("Generated Hill Curbs").GetComponent<MeshFilter>().sharedMesh;
		Assert.That(barriers.bounds.max.y, Is.EqualTo(3f).Within(0.001f));
	}

	[TestCase(false)]
	[TestCase(true)]
	public void SetMapData_CurbMaterial_AppliesSharedMaterialAndPreservesOptionalHillOverride(bool overrideHill)
	{
		Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
		Assert.That(shader, Is.Not.Null, "The project must provide URP Lit or Standard for the material fixture.");
		var commonMaterial = new Material(shader);
		var overrideMaterial = new Material(shader);
		try
		{
			var settings = new SerializedObject(generator);
			settings.FindProperty("curbMaterial").objectReferenceValue = commonMaterial;
			settings.FindProperty("hillCurbMaterial").objectReferenceValue = overrideHill ? overrideMaterial : null;
			settings.ApplyModifiedPropertiesWithoutUndo();

			generator.SetMapData(CsvParaser.GetMapDataFromText(
				"name,layer,start_x,start_y,end_x,end_y\n"
				+ "Line,Hill,1000,1000,31000,1000\n"
				+ "Line,Hill,1000,9000,31000,9000\n"
				+ "Line,Hill,11000,1000,11000,9000\n"
				+ "Line,Hill,21000,1000,21000,9000\n"
				+ "Line,Curb,1000,9500,31000,9500"));

			Assert.That(mapObject.transform.Find("Generated Curb Layer").GetComponent<MeshRenderer>().sharedMaterial,
				Is.SameAs(commonMaterial));
			Assert.That(mapObject.transform.Find("Generated Hill Curbs").GetComponent<MeshRenderer>().sharedMaterial,
				Is.SameAs(overrideHill ? overrideMaterial : commonMaterial));

			GenerateLine("Line");

			Assert.That(commonMaterial != null, Is.True, "Regeneration must not destroy the assigned material.");
			Assert.That(overrideMaterial != null, Is.True);
		}
		finally
		{
			generator.ClearMap();
			Object.DestroyImmediate(commonMaterial);
			Object.DestroyImmediate(overrideMaterial);
		}
	}

	#endregion
}
