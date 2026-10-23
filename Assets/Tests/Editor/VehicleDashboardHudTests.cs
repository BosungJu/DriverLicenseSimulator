using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

// Runtime integration tests for generated Canvas UI and MonoBehaviour lifecycle.
public class VehicleDashboardHudTests
{
	private GameObject vehicle;
	private Canvas instrumentCanvas;
	private Canvas centerCanvas;
	private VehicleDashboardHud dashboard;

	#region Setup and Teardown

	[UnitySetUp]
	public IEnumerator SetUp()
	{
		yield return new EnterPlayMode();
		vehicle = new GameObject("DashboardTestVehicle");
		vehicle.SetActive(false);
		var body = vehicle.AddComponent<Rigidbody>();
		body.useGravity = false;
		body.linearDamping = 0f;
		vehicle.AddComponent<CarController>().enabled = false;
		vehicle.AddComponent<VehicleSystems>().enabled = false;
		centerCanvas = CreateCanvas("Display_Center");
		instrumentCanvas = CreateCanvas("Display_InstrumentCluster");
		dashboard = vehicle.AddComponent<VehicleDashboardHud>();
		var serializedDashboard = new SerializedObject(dashboard);
		serializedDashboard.FindProperty("showScreenOverlay").boolValue = false;
		serializedDashboard.ApplyModifiedPropertiesWithoutUndo();
		vehicle.SetActive(true);
		yield return null;
	}

	[UnityTearDown]
	public IEnumerator TearDown()
	{
		if (vehicle != null)
		{
			Object.Destroy(vehicle);
			yield return null;
		}
		yield return new ExitPlayMode();
	}

	#endregion

	#region Canvas Display

	[UnityTest]
	public IEnumerator Dashboard_InstrumentCanvasOnly_PreservesCanvasAndDisplaysSpeed()
	{
		Assert.IsNull(centerCanvas.transform.Find("Dashboard"));
		Transform panel = instrumentCanvas.transform.Find("Dashboard");
		Assert.IsNotNull(panel);
		Assert.AreEqual(new Vector2(0.4f, 0.18f), ((RectTransform)instrumentCanvas.transform).sizeDelta);
		Assert.AreEqual(Vector3.zero, instrumentCanvas.transform.localPosition);
		Assert.AreEqual(Vector3.one, instrumentCanvas.transform.localScale);

		vehicle.GetComponent<Rigidbody>().linearVelocity = new Vector3(0f, 0f, 10f);
		yield return null;
		yield return null;

		Assert.AreEqual("36", panel.Find("Speed").GetComponent<Text>().text);
		Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 0f, 54f), panel.Find("GaugeNeedle").localRotation), 0.01f);
		foreach (RawImage image in panel.GetComponentsInChildren<RawImage>())
		{
			Rect imageRect = image.rectTransform.rect;
			Assert.AreEqual((float)image.texture.width / image.texture.height, imageRect.width / imageRect.height, 0.001f);
		}
		foreach (Graphic graphic in panel.GetComponentsInChildren<Graphic>())
		{
			Assert.IsFalse(graphic.raycastTarget);
		}
	}

	[UnityTest]
	public IEnumerator Dashboard_DisableEnableAndDestroy_UpdatesGeneratedPanelLifetime()
	{
		Transform panel = instrumentCanvas.transform.Find("Dashboard");
		dashboard.enabled = false;
		Assert.IsFalse(panel.gameObject.activeSelf);
		dashboard.enabled = true;
		Assert.IsTrue(panel.gameObject.activeSelf);
		Assert.AreEqual(1, instrumentCanvas.transform.childCount);

		Object.Destroy(dashboard);
		yield return null;
		yield return null;

		Assert.IsTrue(panel == null);
		Assert.AreEqual(0, centerCanvas.transform.childCount);
	}

	#endregion

	private Canvas CreateCanvas(string displayName)
	{
		var display = new GameObject(displayName);
		display.transform.SetParent(vehicle.transform, false);
		var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
		canvasObject.transform.SetParent(display.transform, false);
		((RectTransform)canvasObject.transform).sizeDelta = new Vector2(0.4f, 0.18f);
		var canvas = canvasObject.GetComponent<Canvas>();
		canvas.renderMode = RenderMode.WorldSpace;
		return canvas;
	}
}
