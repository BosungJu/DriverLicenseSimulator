using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;

public class SideMirrorTests
{
	private GameObject vehicle;
	private Mesh glassMesh;
	private Material sourceMaterial;
	private Material runtimeMaterial;
	private RenderTexture mirrorTexture;

	[UnityTearDown]
	public IEnumerator TearDown()
	{
		if (vehicle != null)
		{
			Object.DestroyImmediate(vehicle);
		}

		if (glassMesh != null)
		{
			Object.DestroyImmediate(glassMesh);
		}

		if (runtimeMaterial != null)
		{
			Object.DestroyImmediate(runtimeMaterial);
		}

		if (sourceMaterial != null)
		{
			Object.DestroyImmediate(sourceMaterial);
		}

		if (mirrorTexture != null)
		{
			Object.DestroyImmediate(mirrorTexture);
		}

		if (EditorApplication.isPlaying)
		{
			yield return new ExitPlayMode();
		}
	}

	[TestCase(true, true)]
	[TestCase(false, true)]
	[TestCase(true, false)]
	[TestCase(false, false)]
	public void LateUpdate_PositionMode_PreservesConfiguredLocalRotation(bool cameraIsChildOfGlass, bool alignToGlassCenter)
	{
		vehicle = new GameObject("Mirror Test Vehicle");
		var glass = new GameObject("Mirror Glass");
		glass.transform.SetParent(vehicle.transform, false);
		glass.transform.localPosition = new Vector3(2f, 1f, 3f);
		var mirror = glass.AddComponent<SideMirror>();
		mirror.alignCameraToGlassCenter = alignToGlassCenter;
		mirror.mirrorRenderer = glass.AddComponent<MeshRenderer>();
		glassMesh = new Mesh();
		glassMesh.vertices = new[]
		{
			new Vector3(0.5f, 0f, 0.25f),
			new Vector3(1.5f, 0f, 0.25f),
			new Vector3(1.5f, 1f, 0.25f),
			new Vector3(0.5f, 1f, 0.25f)
		};
		glassMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
		glassMesh.RecalculateBounds();
		glass.AddComponent<MeshFilter>().sharedMesh = glassMesh;

		var driverEye = new GameObject("Driver Eye");
		driverEye.transform.SetParent(vehicle.transform, false);
		driverEye.transform.localPosition = new Vector3(3f, 1.5f, 1.25f);
		mirror.driverEye = driverEye.transform;

		var cameraObject = new GameObject("Mirror Camera");
		cameraObject.transform.SetParent(cameraIsChildOfGlass ? glass.transform : vehicle.transform, false);
		cameraObject.transform.localPosition = new Vector3(-0.728f, -1.107f, -1.086f);
		Quaternion configuredLocalRotation = Quaternion.Euler(20f, 80f, 270f);
		cameraObject.transform.localRotation = configuredLocalRotation;
		mirror.mirrorCam = cameraObject.AddComponent<Camera>();
		mirror.mirrorCam.enabled = false;
		Vector3 initialCameraPosition = cameraObject.transform.position;

		mirror.SendMessage("LateUpdate");

		Vector3 expectedPositionInVehicle = alignToGlassCenter ? new Vector3(3f, 1.5f, 3.25f) : initialCameraPosition;
		Assert.Less(Vector3.Distance(expectedPositionInVehicle, cameraObject.transform.position), 0.0001f);
		Assert.Less(Quaternion.Angle(configuredLocalRotation, cameraObject.transform.localRotation), 0.01f);

		vehicle.transform.SetPositionAndRotation(new Vector3(10f, 2f, -4f), Quaternion.Euler(0f, 45f, 0f));
		Vector3 expectedCameraPosition = vehicle.transform.TransformPoint(expectedPositionInVehicle);
		driverEye.transform.localPosition += Vector3.right;
		mirror.SendMessage("LateUpdate");

		Assert.Less(Vector3.Distance(expectedCameraPosition, cameraObject.transform.position), 0.0001f);
		Assert.Less(Quaternion.Angle(configuredLocalRotation, cameraObject.transform.localRotation), 0.01f);
		Assert.Less(Quaternion.Angle(vehicle.transform.rotation * configuredLocalRotation, cameraObject.transform.rotation), 0.01f);

		Quaternion adjustedLocalRotation = Quaternion.Euler(-10f, 65f, 5f);
		cameraObject.transform.localRotation = adjustedLocalRotation;
		mirror.SendMessage("LateUpdate");

		Assert.Less(Quaternion.Angle(adjustedLocalRotation, cameraObject.transform.localRotation), 0.01f);
	}

	// Runtime integration: material instantiation requires Play Mode.
	[UnityTest]
	public IEnumerator Start_ConfiguredCamera_PreservesFovClippingAndDefaultManualPosition()
	{
		return VerifyMirrorTextureCoordinates(false);
	}

	[UnityTest]
	public IEnumerator Start_MeshWithUv_PreservesExistingTextureCoordinates()
	{
		return VerifyMirrorTextureCoordinates(true);
	}

	private IEnumerator VerifyMirrorTextureCoordinates(bool hasExistingUv)
	{
		yield return new EnterPlayMode();
		vehicle = new GameObject("Mirror Camera Settings Test");
		SideMirror mirror = vehicle.AddComponent<SideMirror>();
		// 후방거울은 기존 반전 기본값, 사이드미러는 투영 UV에 맞춰 반전을 끈다.
		Assert.IsTrue(mirror.flipHorizontal);
		mirror.flipHorizontal = hasExistingUv;
		mirror.mirrorRenderer = vehicle.AddComponent<MeshRenderer>();
		glassMesh = new Mesh();
		glassMesh.vertices = new[]
		{
			new Vector3(0.25f, 0f, 0f),
			new Vector3(0.25f, 2f, 0f),
			new Vector3(0.25f, 2f, 1f),
			new Vector3(0.25f, 0f, 1f)
		};
		glassMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
		glassMesh.RecalculateBounds();
		if (hasExistingUv)
		{
			glassMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
		}
		vehicle.AddComponent<MeshFilter>().sharedMesh = glassMesh;
		glassMesh.UploadMeshData(true);
		Shader shader = Shader.Find("Universal Render Pipeline/Lit");
		Assert.IsNotNull(shader);
		sourceMaterial = new Material(shader);
		sourceMaterial.SetColor("_BaseColor", Color.black);
		sourceMaterial.SetFloat("_Metallic", 1f);
		mirror.mirrorRenderer.sharedMaterial = sourceMaterial;
		mirror.displayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Car/BasicModel/SonataURP/Materials/Material_Mirror_Display.mat");
		Assert.IsNotNull(mirror.displayMaterial);
		var cameraObject = new GameObject("Mirror Camera");
		cameraObject.transform.SetParent(vehicle.transform, false);
		mirror.mirrorCam = cameraObject.AddComponent<Camera>();
		mirror.mirrorCam.enabled = false;
		mirror.mirrorCam.fieldOfView = 60f;
		mirror.mirrorCam.nearClipPlane = 0.1f;
		mirror.mirrorCam.farClipPlane = 800f;
		Vector3 configuredPosition = new Vector3(1f, 2f, 3f);
		cameraObject.transform.localPosition = configuredPosition;

		try
		{
			yield return null;

			Assert.IsFalse(mirror.alignCameraToGlassCenter);
			Assert.AreEqual(configuredPosition, cameraObject.transform.localPosition);
			Assert.AreEqual(60f, mirror.mirrorCam.fieldOfView, 0.001f);
			Assert.AreEqual(0.1f, mirror.mirrorCam.nearClipPlane, 0.001f);
			Assert.AreEqual(800f, mirror.mirrorCam.farClipPlane, 0.001f);
			Assert.IsNotNull(mirror.mirrorCam.targetTexture);
			Material activeMaterial = mirror.mirrorRenderer.sharedMaterial;
			Assert.AreNotSame(sourceMaterial, activeMaterial);
			Assert.AreNotSame(mirror.displayMaterial, activeMaterial);
			Assert.AreEqual("DriverLicenseSimulator/Mirror Display", activeMaterial.shader.name);
			Assert.IsFalse(ShaderUtil.ShaderHasError(activeMaterial.shader));
			Assert.AreEqual(hasExistingUv ? 0f : 1f, activeMaterial.GetFloat("_UsePlanarUV"));
			Assert.AreEqual(hasExistingUv, glassMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0));
			Assert.AreSame(glassMesh, vehicle.GetComponent<MeshFilter>().sharedMesh);
			if (!hasExistingUv)
			{
				Assert.AreEqual(new Vector4(0.25f, 0f, 0f, 0f), activeMaterial.GetVector("_MirrorUvMin"));
				Assert.AreEqual(new Vector4(0f, 2f, 1f, 0f), activeMaterial.GetVector("_MirrorUvSize"));
				Assert.AreEqual(new Vector4(0f, 1f, 0f, 0f), activeMaterial.GetVector("_MirrorUvHorizontal"));
				Assert.AreEqual(new Vector4(0f, 0f, 1f, 0f), activeMaterial.GetVector("_MirrorUvVertical"));
			}
			Assert.AreEqual(Color.white, activeMaterial.GetColor("_BaseColor"));
			Assert.AreSame(mirror.mirrorCam.targetTexture, activeMaterial.GetTexture("_BaseMap"));
			Assert.AreEqual(new Vector2(hasExistingUv ? -1f : 1f, 1f), activeMaterial.GetTextureScale("_BaseMap"));
			Assert.AreEqual(new Vector2(hasExistingUv ? 1f : 0f, 0f), activeMaterial.GetTextureOffset("_BaseMap"));
			Assert.AreEqual(Color.black, sourceMaterial.GetColor("_BaseColor"));
		}
		finally
		{
			mirrorTexture = mirror.mirrorCam.targetTexture;
			runtimeMaterial = mirror.mirrorRenderer.sharedMaterial;
		}
	}
}
