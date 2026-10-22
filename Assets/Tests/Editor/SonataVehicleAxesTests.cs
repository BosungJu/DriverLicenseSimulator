using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

public class SonataVehicleAxesTests
{
	private GameObject parent;
	private GameObject vehicle;

	[UnityTearDown]
	public IEnumerator TearDown()
	{
		if (parent != null)
		{
			UnityEngine.Object.DestroyImmediate(parent);
		}
		if (EditorApplication.isPlaying)
		{
			yield return new ExitPlayMode();
		}
	}

	[Test]
	public void Vehicle_RigMatchesModel_DriverCameraAndLightsFaceForward()
	{
		CreateVehicle();
		Transform head = Find("Head");
		Transform vision = Find("Vision");
		Assert.AreEqual(-0.44f, head.position.x, 0.001f);
		Assert.AreEqual(-0.199f, head.position.z, 0.001f);
		Assert.Less((vision.position - head.position).magnitude, 0.001f);
		Assert.Greater(Vector3.Dot(vision.forward, Vector3.forward), 0.9f);
		Assert.Less(Quaternion.Angle(Find("SonataURP_Model").rotation, Quaternion.identity), 0.01f);
		foreach (Light light in vehicle.GetComponent<VehicleSystems>().headlights)
		{
			Assert.Greater(light.transform.position.z, 2f);
			Assert.Greater(Vector3.Dot(light.transform.forward, Vector3.forward), 0.8f);
		}
		SideMirror[] mirrors = vehicle.GetComponentsInChildren<SideMirror>(true);
		Assert.AreEqual(3, mirrors.Length);
		foreach (SideMirror mirror in mirrors)
		{
			Assert.AreSame(vision, mirror.driverEye);
			Assert.IsNotNull(mirror.mirrorRenderer);
			Assert.IsNotNull(mirror.mirrorCam);
			Transform glass = mirror.mirrorRenderer.transform;
			Transform reflectionAxis = glass.name.StartsWith("MirrorGlass_") || glass.name.StartsWith("RearViewMirrorGlass") ? glass.parent : glass;
			Assert.IsTrue(reflectionAxis.name.StartsWith("SideMirror_") || reflectionAxis.name.StartsWith("RearViewMirrorPivot"), glass.name);
			Assert.Greater(Vector3.Dot(glass.forward, reflectionAxis.forward), 0.9999f, glass.name);
		}
	}

	[Test]
	public void Vehicle_WheelVisualRotation_DoesNotMoveColliders()
	{
		CreateVehicle();
		CarController controller = vehicle.GetComponent<CarController>();
		Transform[] wheels = { controller.frontLeftWheel, controller.frontRightWheel, controller.rearLeftWheel, controller.rearRightWheel };
		WheelCollider[] colliders = { controller.frontLeftCollider, controller.frontRightCollider, controller.rearLeftCollider, controller.rearRightCollider };
		for (int index = 0; index < wheels.Length; index++)
		{
			Transform wheel = wheels[index];
			Transform anchor = colliders[index].transform;
			Assert.AreNotSame(wheel, anchor);
			Assert.AreSame(anchor, wheel.parent);
			Assert.Greater(wheel.GetComponentsInChildren<Renderer>(true).Length, 0);
			Assert.Less(Quaternion.Angle(wheel.rotation, Quaternion.identity), 0.01f);
			Vector3 position = anchor.position;
			Quaternion rotation = anchor.rotation;
			wheel.SetPositionAndRotation(wheel.position + Vector3.up * 0.02f, Quaternion.Euler(30f, 5f, 0f));
			Assert.AreEqual(position, anchor.position);
			Assert.AreEqual(rotation, anchor.rotation);
		}
	}

	[Test]
	public void Vehicle_WiperClip_AllBindingsResolveFromAssignedAnimator()
	{
		CreateVehicle();
		WiperController controller = vehicle.GetComponent<WiperController>();
		Assert.AreSame(vehicle.GetComponent<Animator>(), controller.WiperAnimator);
		AnimationClip clip = controller.WiperAnimator.runtimeAnimatorController.animationClips[0];
		EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
		Assert.Greater(bindings.Length, 0);
		foreach (EditorCurveBinding binding in bindings)
		{
			Assert.IsNotNull(controller.WiperAnimator.transform.Find(binding.path), binding.path);
			Assert.IsTrue(binding.path.Contains("WiperPivot_"), binding.path);
		}
	}

	[UnityTest]
	public IEnumerator Vehicle_WiperController_AnimatesActualPrefabAndReturnsToPark()
	{
		yield return new EnterPlayMode();
		CreateVehicle();
		foreach (MonoBehaviour behaviour in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
		{
			if (behaviour != null && !(behaviour is WiperController) && !(behaviour is CarController))
			{
				behaviour.enabled = false;
			}
		}
		vehicle.GetComponent<Rigidbody>().isKinematic = true;
		CarController carController = vehicle.GetComponent<CarController>();
		WheelCollider[] colliders = { carController.frontLeftCollider, carController.frontRightCollider, carController.rearLeftCollider, carController.rearRightCollider };
		Transform[] wheels = { carController.frontLeftWheel, carController.frontRightWheel, carController.rearLeftWheel, carController.rearRightWheel };
		Vector3[] colliderPositions = Array.ConvertAll(colliders, collider => collider.transform.localPosition);
		WiperController controller = vehicle.GetComponent<WiperController>();
		Transform[] pivots = Array.FindAll(vehicle.GetComponentsInChildren<Transform>(true), candidate => candidate.name.StartsWith("WiperPivot_"));
		Assert.AreEqual(6, pivots.Length);
		Quaternion[] park = Array.ConvertAll(pivots, pivot => pivot.localRotation);
		parent.SetActive(true);
		yield return null;
		for (int index = 0; index < pivots.Length; index++)
		{
			Assert.Less(Quaternion.Angle(park[index], pivots[index].localRotation), 0.05f, pivots[index].name);
		}
		controller.SetWiping(true);
		float elapsed = 0f;
		while (elapsed < 0.6f)
		{
			elapsed += Time.deltaTime;
			yield return null;
		}
		for (int index = 0; index < pivots.Length; index++)
		{
			Assert.Greater(Quaternion.Angle(park[index], pivots[index].localRotation), 10f, pivots[index].name);
		}
		controller.SetWiping(false);
		elapsed = 0f;
		while (elapsed < 2.2f)
		{
			elapsed += Time.deltaTime;
			yield return null;
		}
		for (int index = 0; index < pivots.Length; index++)
		{
			Assert.Less(Quaternion.Angle(park[index], pivots[index].localRotation), 0.05f, pivots[index].name);
		}
		for (int index = 0; index < colliders.Length; index++)
		{
			Assert.Less((colliders[index].transform.localPosition - colliderPositions[index]).magnitude, 0.0001f);
			Assert.Less(Quaternion.Angle(colliders[index].transform.rotation, Quaternion.identity), 0.05f);
			Assert.Less(Quaternion.Angle(wheels[index].rotation, Quaternion.identity), 0.05f);
		}
	}

	private void CreateVehicle()
	{
		parent = new GameObject("Vehicle Test");
		parent.SetActive(false);
		vehicle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Car/Prefabs/Car.prefab"), parent.transform);
		vehicle.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
	}

	private Transform Find(string name)
	{
		return Array.Find(vehicle.GetComponentsInChildren<Transform>(true), candidate => candidate.name == name);
	}
}

