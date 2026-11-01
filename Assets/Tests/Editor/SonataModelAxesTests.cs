using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class SonataModelAxesTests
{
	private const string AssetRoot = "Assets/Resources/Car/BasicModel/SonataURP";
	private const float HeadlinerCheckMinHeightMeters = 1.15f;
	private const float RoofRayHeightMeters = 2f;
	// The model uses a 12 mm inset; allow 4 mm for import and collider precision.
	private const float MinimumRoofClearanceMeters = 0.008f;
	private const int MinimumCheckedHeadlinerVertices = 100;
	private const int HeadlinerTriangleSubdivisions = 8;
	private GameObject model;

	[SetUp]
	public void SetUp()
	{
		model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/SonataURP_Improved.fbx"));
	}

	[TearDown]
	public void TearDown()
	{
		UnityEngine.Object.DestroyImmediate(model);
	}

	[Test]
	public void Model_ZeroRotationAndUnitScale_IsGroundedAndFacesPositiveZ()
	{
		Transform hierarchy = model.transform.Find("SonataURP_Model");
		Assert.IsNotNull(hierarchy);
		foreach (Transform root in new[] { model.transform, hierarchy, hierarchy.Find("LOD0"), hierarchy.Find("LOD1"), hierarchy.Find("LOD2") })
		{
			Assert.Less(Quaternion.Angle(Quaternion.identity, root.localRotation), 0.01f, root.name);
			Assert.Less((root.localScale - Vector3.one).magnitude, 0.0001f, root.name);
			Assert.Less(root.localPosition.magnitude, 0.0001f, root.name);
		}

		for (int level = 0; level < 3; level++)
		{
			Transform lod = hierarchy.Find("LOD" + level);
			Transform[] wheels = Array.FindAll(lod.GetComponentsInChildren<Transform>(true), candidate => candidate.name.StartsWith("Wheel_") && !candidate.name.Contains("__"));
			Assert.AreEqual(4, wheels.Length);
			foreach (Transform wheel in wheels)
			{
				Assert.Less(Quaternion.Angle(Quaternion.identity, wheel.rotation), 0.01f, wheel.name);
				Assert.Less((wheel.lossyScale - Vector3.one).magnitude, 0.0001f, wheel.name);
				Assert.Greater(wheel.position.y, 0.3f);
				Assert.Less(wheel.position.y, 0.4f);
				Assert.IsTrue(wheel.name.Contains("Front") ? wheel.position.z > 1f : wheel.position.z < -1f, wheel.name);
				Assert.IsTrue(wheel.name.Contains("Left") ? wheel.position.x < -0.7f : wheel.position.x > 0.7f, wheel.name);
			}
		}

		Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
		Bounds bounds = renderers[0].bounds;
		foreach (Renderer renderer in renderers)
		{
			bounds.Encapsulate(renderer.bounds);
		}
		Assert.AreEqual(0f, bounds.min.y, 0.001f);
		Assert.That(bounds.size.y, Is.InRange(1.4f, 1.6f));
		Assert.That(bounds.size.z, Is.InRange(4.8f, 5.1f));
		Assert.Greater(bounds.size.z, bounds.size.x);
	}

	[Test]
	public void Model_StaticChildren_HaveZeroLocalRotationAndUnitScale()
	{
		foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
		{
			bool hasFunctionalAxis = child.name.StartsWith("WiperPivot_")
				|| child.name.StartsWith("SteeringWheelPivot")
				|| child.name.StartsWith("RearViewMirrorPivot")
				|| child.name.StartsWith("SideMirror_Left")
				|| child.name.StartsWith("SideMirror_Right");
			Assert.Less((child.localScale - Vector3.one).magnitude, 0.0001f, child.name);
			if (!hasFunctionalAxis)
			{
				Assert.Less(Quaternion.Angle(child.localRotation, Quaternion.identity), 0.01f, child.name);
			}
		}
	}

	[Test]
	public void Model_Headliner_RemainsInsideRoofAtEveryLod()
	{
		for (int level = 0; level < 3; level++)
		{
			Transform lod = model.transform.Find("SonataURP_Model/LOD" + level);
			Assert.IsNotNull(lod, "LOD" + level);
			MeshFilter[] meshes = lod.GetComponentsInChildren<MeshFilter>(true);
			MeshFilter roof = Array.Find(meshes, mesh => mesh.name == "Roof__Crimson metallic clearcoat");
			MeshFilter headliner = Array.Find(meshes, mesh => mesh.name == "Roof__Pale headliner");
			Assert.IsNotNull(roof, "LOD" + level + " roof");
			Assert.IsNotNull(headliner, "LOD" + level + " headliner");
			MeshCollider roofCollider = roof.gameObject.AddComponent<MeshCollider>();
			roofCollider.sharedMesh = roof.sharedMesh;
			Physics.SyncTransforms();

			int checkedVertices = 0;
			foreach (Vector3 vertex in headliner.sharedMesh.vertices)
			{
				Vector3 worldPosition = headliner.transform.TransformPoint(vertex);
				if (worldPosition.y < HeadlinerCheckMinHeightMeters)
				{
					continue;
				}

				Ray roofRay = new Ray(new Vector3(worldPosition.x, RoofRayHeightMeters, worldPosition.z), Vector3.down);
				if (!roofCollider.Raycast(roofRay, out RaycastHit roofHit, RoofRayHeightMeters))
				{
					continue;
				}

				Assert.LessOrEqual(worldPosition.y, roofHit.point.y - MinimumRoofClearanceMeters,
					"LOD" + level + " headliner protrudes through roof at " + worldPosition);
				checkedVertices++;
			}

			Assert.Greater(checkedVertices, MinimumCheckedHeadlinerVertices, "LOD" + level + " roof coverage");
			AssertHeadlinerFacesInsideRoof(headliner, roofCollider, level);
		}
	}

	private static void AssertHeadlinerFacesInsideRoof(MeshFilter headliner, MeshCollider roofCollider, int level)
	{
		Vector3[] vertices = headliner.sharedMesh.vertices;
		int[] triangles = headliner.sharedMesh.triangles;
		for (int triangle = 0; triangle < triangles.Length; triangle += 3)
		{
			Vector3 first = headliner.transform.TransformPoint(vertices[triangles[triangle]]);
			Vector3 second = headliner.transform.TransformPoint(vertices[triangles[triangle + 1]]);
			Vector3 third = headliner.transform.TransformPoint(vertices[triangles[triangle + 2]]);
			// Safe vertices alone do not prevent a face from crossing the curved roof.
			for (int firstStep = 0; firstStep <= HeadlinerTriangleSubdivisions; firstStep++)
			{
				for (int secondStep = 0; secondStep <= HeadlinerTriangleSubdivisions - firstStep; secondStep++)
				{
					float secondWeight = (float)firstStep / HeadlinerTriangleSubdivisions;
					float thirdWeight = (float)secondStep / HeadlinerTriangleSubdivisions;
					Vector3 sample = first * (1f - secondWeight - thirdWeight) + second * secondWeight + third * thirdWeight;
					if (sample.y < HeadlinerCheckMinHeightMeters)
					{
						continue;
					}

					Ray roofRay = new Ray(new Vector3(sample.x, RoofRayHeightMeters, sample.z), Vector3.down);
					if (roofCollider.Raycast(roofRay, out RaycastHit roofHit, RoofRayHeightMeters))
					{
						Assert.LessOrEqual(sample.y, roofHit.point.y,
							"LOD" + level + " headliner face protrudes through roof at " + sample);
					}
				}
			}
		}
	}

	[Test]
	public void WiperAnimation_NormalizedModel_PreservesParkAndSweepsAllLods()
	{
		AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetRoot + "/Animations/Animation_Wiper_Sweep.anim");
		Transform[] pivots = Array.FindAll(model.GetComponentsInChildren<Transform>(true), candidate => candidate.name.StartsWith("WiperPivot_"));
		Assert.AreEqual(6, pivots.Length);
		Quaternion[] rest = Array.ConvertAll(pivots, pivot => pivot.localRotation);
		Vector3[] positions = Array.ConvertAll(pivots, pivot => pivot.position);
		clip.SampleAnimation(model, 0f);
		for (int index = 0; index < pivots.Length; index++)
		{
			Assert.Less(Quaternion.Angle(rest[index], pivots[index].localRotation), 0.05f, pivots[index].name);
		}
		clip.SampleAnimation(model, clip.length * 0.5f);
		for (int index = 0; index < pivots.Length; index++)
		{
			Assert.AreEqual(65f, Quaternion.Angle(rest[index], pivots[index].localRotation), 0.1f, pivots[index].name);
			Assert.Less((positions[index] - pivots[index].position).magnitude, 0.0001f);
		}
		clip.SampleAnimation(model, clip.length);
		for (int index = 0; index < pivots.Length; index++)
		{
			Assert.Less(Quaternion.Angle(rest[index], pivots[index].localRotation), 0.05f, pivots[index].name);
		}
	}
}
