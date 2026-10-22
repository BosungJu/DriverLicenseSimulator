using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

// Runtime integration tests: real MonoBehaviour frames, Animator and Input System.
public class WiperControllerTests
{
	private const string AssetRoot = "Assets/Resources/Car/BasicModel/SonataURP";
	private GameObject vehicle;
	private WiperController controller;
	private Animator animator;
	private Transform leftPivot;
	private Transform rightPivot;
	private Quaternion leftPark;
	private Quaternion rightPark;
	private float previousCaptureDeltaTime;
	private InputSettings originalInputSettings;
	private InputSettings testInputSettings;
	private InputAction action;
	private InputActionReference actionReference;
	private InputActionAsset actionAsset;
	private Gamepad gamepad;

	#region Setup and Teardown

	[UnitySetUp]
	public IEnumerator SetUp()
	{
		yield return new EnterPlayMode();
		previousCaptureDeltaTime = Time.captureDeltaTime;
		originalInputSettings = InputSystem.settings;
		testInputSettings = Object.Instantiate(originalInputSettings);
		InputSystem.settings = testInputSettings;
		Time.captureDeltaTime = 0.1f;
	}

	[UnityTearDown]
	public IEnumerator TearDown()
	{
		if (vehicle != null)
		{
			Object.DestroyImmediate(vehicle);
		}

		if (gamepad != null)
		{
			InputSystem.RemoveDevice(gamepad);
		}

		if (actionReference != null)
		{
			Object.DestroyImmediate(actionReference);
		}

		action?.Dispose();
		if (actionAsset != null)
		{
			Object.DestroyImmediate(actionAsset);
		}
		InputSystem.settings = originalInputSettings;
		Object.DestroyImmediate(testInputSettings);
		Time.captureDeltaTime = previousCaptureDeltaTime;
		yield return new ExitPlayMode();
	}

	#endregion

	#region Animation Behavior

	[UnityTest]
	public IEnumerator AnimatorMode_OffFinishesCurrentCycle_ThenRemainsParked()
	{
		CreateAnimatorVehicle();
		vehicle.SetActive(true);
		yield return null;
		CapturePark();
		yield return Frames(3);
		AssertParked();

		controller.SetWiping(true);
		yield return Frames(5);
		Assert.IsTrue(controller.IsWiping);
		Assert.Greater(Quaternion.Angle(leftPark, leftPivot.localRotation), 5f);
		Assert.Greater(Quaternion.Angle(rightPark, rightPivot.localRotation), 5f);

		controller.SetWiping(false);
		Assert.IsFalse(controller.IsWiping);
		Assert.Greater(Quaternion.Angle(leftPark, leftPivot.localRotation), 5f);
		yield return Frames(25);
		AssertParked();
		yield return Frames(5);
		AssertParked();
	}

	[UnityTest]
	public IEnumerator AnimatorMode_QuickOffOn_ContinuesAndCanParkAgain()
	{
		CreateAnimatorVehicle();
		vehicle.SetActive(true);
		yield return null;
		CapturePark();
		controller.SetWiping(true);
		yield return Frames(5);
		controller.SetWiping(false);
		yield return Frames(2);
		controller.SetWiping(true);
		yield return Frames(5);
		Assert.IsTrue(controller.IsWiping);
		Assert.Greater(Quaternion.Angle(leftPark, leftPivot.localRotation), 5f);
		controller.SetWiping(false);
		yield return Frames(25);
		AssertParked();
	}

	[UnityTest]
	public IEnumerator AnimatorMode_DisableAndEnable_ResetsAndDoesNotResume()
	{
		CreateAnimatorVehicle();
		vehicle.SetActive(true);
		yield return null;
		CapturePark();
		controller.SetWiping(true);
		yield return Frames(5);
		controller.enabled = false;
		Assert.IsFalse(controller.IsWiping);
		AssertParked();
		yield return Frames(3);
		AssertParked();
		controller.enabled = true;
		yield return Frames(3);
		Assert.IsFalse(controller.IsWiping);
		AssertParked();
	}

	[UnityTest]
	public IEnumerator AnimatorMode_MissingState_WarnsAndDoesNotFallBackToDirectRotation()
	{
		CreateAnimatorVehicle();
		var settings = new SerializedObject(controller);
		settings.FindProperty("animatorStateName").stringValue = "Missing State";
		settings.ApplyModifiedPropertiesWithoutUndo();
		CapturePark();
		LogAssert.Expect(LogType.Warning, "[WiperController] Animator state 'Missing State' was not found.");
		vehicle.SetActive(true);
		yield return null;
		controller.SetWiping(true);
		yield return Frames(5);
		Assert.AreEqual(0f, animator.speed);
		Assert.IsFalse(controller.IsWiping);
		AssertParked();
	}

	[UnityTest]
	public IEnumerator AnimatorMode_BecomesAvailableLater_InitializesWithoutControllerRestart()
	{
		CreateAnimatorVehicle();
		animator.enabled = false;
		LogAssert.Expect(LogType.Warning, "[WiperController] The assigned Animator must be enabled and active.");
		vehicle.SetActive(true);
		yield return null;
		controller.SetWiping(true);
		Assert.IsFalse(controller.IsWiping);
		animator.enabled = true;
		yield return Frames(2);
		CapturePark();
		controller.SetWiping(true);
		yield return Frames(5);
		Assert.IsTrue(controller.IsWiping);
		Assert.Greater(Quaternion.Angle(leftPark, leftPivot.localRotation), 5f);
		LogAssert.Expect(LogType.Warning, "[WiperController] The assigned Animator must be enabled and active.");
		animator.enabled = false;
		yield return Frames(2);
		Assert.IsFalse(controller.IsWiping);
		animator.enabled = true;
		yield return Frames(2);
		AssertParked();
		Assert.IsFalse(controller.IsWiping);
	}

	#endregion

	#region Input and Legacy Behavior

	[UnityTest]
	public IEnumerator GamepadPress_TogglesOncePerPress_AndInputIsDisabledWithComponent()
	{
		CreateAnimatorVehicle();
		InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
		InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
		InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
		gamepad = InputSystem.AddDevice<Gamepad>();
		actionAsset = ScriptableObject.CreateInstance<InputActionAsset>();
		InputActionMap actionMap = actionAsset.AddActionMap("Car");
		actionMap.devices = new InputDevice[] { gamepad };
		action = actionMap.AddAction("Wiper", InputActionType.Button, "<Gamepad>/buttonSouth");
		actionReference = InputActionReference.Create(action);
		controller.wiperAction = actionReference;
		vehicle.SetActive(true);
		yield return null;
		Assert.IsTrue(action.enabled);

		InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
		yield return Frames(2);
		Assert.IsTrue(controller.IsWiping);
		yield return Frames(3);
		Assert.IsTrue(controller.IsWiping);
		InputSystem.QueueStateEvent(gamepad, new GamepadState());
		yield return Frames(2);
		InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
		yield return Frames(2);
		Assert.IsFalse(controller.IsWiping);
		controller.enabled = false;
		Assert.IsFalse(action.enabled);
	}

	[UnityTest]
	public IEnumerator NoAnimator_DirectRotationStillMovesBothPivotsAndParks()
	{
		vehicle = new GameObject("Legacy Wipers");
		vehicle.SetActive(false);
		controller = vehicle.AddComponent<WiperController>();
		leftPivot = new GameObject("Left").transform;
		rightPivot = new GameObject("Right").transform;
		leftPivot.SetParent(vehicle.transform);
		rightPivot.SetParent(vehicle.transform);
		leftPivot.localRotation = Quaternion.Euler(10f, 20f, 30f);
		rightPivot.localRotation = Quaternion.Euler(-5f, 15f, 40f);
		controller.leftPivot = leftPivot;
		controller.rightPivot = rightPivot;
		controller.sweepSpeed = Mathf.PI;
		controller.sweepAngle = 70f;
		CapturePark();
		vehicle.SetActive(true);
		yield return null;
		controller.SetWiping(true);
		yield return Frames(5);
		Assert.Greater(Quaternion.Angle(leftPark, leftPivot.localRotation), 5f);
		Assert.Greater(Quaternion.Angle(rightPark, rightPivot.localRotation), 5f);
		controller.SetWiping(false);
		yield return Frames(25);
		AssertParked();
	}

	#endregion

	#region Test Helpers

	private void CreateAnimatorVehicle()
	{
		vehicle = new GameObject("Wiper Test Vehicle");
		vehicle.SetActive(false);
		GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/SonataURP_Improved.fbx");
		Assert.IsNotNull(source);
		GameObject model = Object.Instantiate(source, vehicle.transform);
		animator = model.GetComponent<Animator>();
		if (animator == null)
		{
			animator = model.AddComponent<Animator>();
		}

		animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetRoot + "/Animations/Animator_Wiper_Sweep.controller");
		Assert.IsNotNull(animator.runtimeAnimatorController);
		animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
		leftPivot = FindPivot(model, "WiperPivot_Left");
		rightPivot = FindPivot(model, "WiperPivot_Right");
		controller = vehicle.AddComponent<WiperController>();
		controller.leftPivot = leftPivot;
		controller.rightPivot = rightPivot;
		var settings = new SerializedObject(controller);
		settings.FindProperty("wiperAnimator").objectReferenceValue = animator;
		settings.ApplyModifiedPropertiesWithoutUndo();
	}

	private static Transform FindPivot(GameObject model, string name)
	{
		foreach (Transform candidate in model.GetComponentsInChildren<Transform>(true))
		{
			if (candidate.name == name)
			{
				return candidate;
			}
		}

		Assert.Fail("Missing pivot: " + name);
		return null;
	}

	private void CapturePark()
	{
		leftPark = leftPivot.localRotation;
		rightPark = rightPivot.localRotation;
	}

	private void AssertParked()
	{
		Assert.Less(Quaternion.Angle(leftPark, leftPivot.localRotation), 0.1f);
		Assert.Less(Quaternion.Angle(rightPark, rightPivot.localRotation), 0.1f);
	}

	private static IEnumerator Frames(int count)
	{
		for (int index = 0; index < count; index++)
		{
			yield return null;
		}
	}

	#endregion
}
