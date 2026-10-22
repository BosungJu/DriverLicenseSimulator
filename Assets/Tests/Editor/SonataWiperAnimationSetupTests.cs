using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

public class SonataWiperAnimationSetupTests
{
	private const string AssetRoot = "Assets/Resources/Car/BasicModel/SonataURP";
	private GameObject root;
	private GameObject model;
	private Object previousSelection;
	private readonly List<string> createdAssets = new List<string>();

	[SetUp]
	public void SetUp()
	{
		previousSelection = Selection.activeObject;
		root = new GameObject("Wiper Setup Test");
		GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/SonataURP_Improved.fbx");
		Assert.IsNotNull(source);
		model = Object.Instantiate(source, root.transform);
		Animator animator = model.GetComponent<Animator>();
		if (animator == null)
		{
			animator = model.AddComponent<Animator>();
		}

		animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetRoot + "/Animations/Animator_Wiper_Sweep.controller");
		Assert.IsNotNull(animator.runtimeAnimatorController);
		Selection.activeGameObject = model;
	}

	[TearDown]
	public void TearDown()
	{
		Object.DestroyImmediate(root);
		foreach (string asset in createdAssets)
		{
			AssetDatabase.DeleteAsset(asset);
		}

		createdAssets.Clear();
		Selection.activeObject = previousSelection;
	}

	[Test]
	public void Setup_ExistingAnimator_ConnectsExistingControllerWithoutReplacingAnimator()
	{
		WiperController controller = root.AddComponent<WiperController>();
		Animator animator = model.GetComponent<Animator>();
		RuntimeAnimatorController originalAnimatorController = animator.runtimeAnimatorController;

		Assert.IsTrue(EditorApplication.ExecuteMenuItem("Tools/Sonata URP/Create Wiper Animation for Selected Model"));

		Assert.AreSame(animator, controller.WiperAnimator);
		Assert.AreSame(originalAnimatorController, animator.runtimeAnimatorController);
		Assert.AreEqual(1, root.GetComponentsInChildren<WiperController>(true).Length);
	}

	[Test]
	public void Setup_MultipleUnrelatedControllers_WarnsWithoutReassigningThem()
	{
		GameObject first = new GameObject("First Controller");
		GameObject second = new GameObject("Second Controller");
		first.transform.SetParent(root.transform);
		second.transform.SetParent(root.transform);
		WiperController firstController = first.AddComponent<WiperController>();
		WiperController secondController = second.AddComponent<WiperController>();
		LogAssert.Expect(LogType.Warning, "[Sonata] Multiple WiperControllers found. Assign Wiper Animator manually on the intended controller.");

		Assert.IsTrue(EditorApplication.ExecuteMenuItem("Tools/Sonata URP/Create Wiper Animation for Selected Model"));

		Assert.IsNull(firstController.WiperAnimator);
		Assert.IsNull(secondController.WiperAnimator);
		Assert.AreEqual(2, root.GetComponentsInChildren<WiperController>(true).Length);
	}

	[Test]
	public void Setup_ControllerAssignedToAnotherAnimator_PreservesThatConnection()
	{
		WiperController controller = root.AddComponent<WiperController>();
		GameObject otherModel = new GameObject("Other Animator");
		otherModel.transform.SetParent(root.transform);
		Animator otherAnimator = otherModel.AddComponent<Animator>();
		var settings = new SerializedObject(controller);
		settings.FindProperty("wiperAnimator").objectReferenceValue = otherAnimator;
		settings.ApplyModifiedPropertiesWithoutUndo();
		LogAssert.Expect(LogType.Warning, "[Sonata] The WiperController is assigned to another Animator. Preserving that connection.");

		Assert.IsTrue(EditorApplication.ExecuteMenuItem("Tools/Sonata URP/Create Wiper Animation for Selected Model"));

		Assert.AreSame(otherAnimator, controller.WiperAnimator);
	}

	[Test]
	public void Setup_NoAnimatorController_CreatesAssetsAndConnectsController()
	{
		WiperController controller = root.AddComponent<WiperController>();
		Animator animator = model.GetComponent<Animator>();
		animator.runtimeAnimatorController = null;

		Assert.IsTrue(EditorApplication.ExecuteMenuItem("Tools/Sonata URP/Create Wiper Animation for Selected Model"));

		if (animator.runtimeAnimatorController != null)
		{
			createdAssets.Add(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController));
			foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
			{
				createdAssets.Add(AssetDatabase.GetAssetPath(clip));
			}
		}

		Assert.IsNotNull(animator.runtimeAnimatorController);
		Assert.AreSame(animator, controller.WiperAnimator);
		Assert.AreEqual(2, createdAssets.Count);
	}
	[Test]
	public void Setup_RenamedAnimatorLayer_UsesActualStatePath()
	{
		string path = AssetDatabase.GenerateUniqueAssetPath(AssetRoot + "/Animations/Animator_Wiper_Test.controller");
		createdAssets.Add(path);
		AnimatorController animationController = AnimatorController.CreateAnimatorControllerAtPath(path);
		AnimatorControllerLayer[] layers = animationController.layers;
		layers[0].name = "Wiper Layer";
		animationController.layers = layers;
		layers[0].stateMachine.AddState("Wiper Sweep");
		model.GetComponent<Animator>().runtimeAnimatorController = animationController;
		WiperController controller = root.AddComponent<WiperController>();

		Assert.IsTrue(EditorApplication.ExecuteMenuItem("Tools/Sonata URP/Create Wiper Animation for Selected Model"));

		var settings = new SerializedObject(controller);
		Assert.AreEqual("Wiper Layer.Wiper Sweep", settings.FindProperty("animatorStateName").stringValue);
	}

	[Test]
	public void Setup_NewAnimationWithAmbiguousControllers_DoesNotCreateAssetsOrAssignAnimator()
	{
		Animator animator = model.GetComponent<Animator>();
		animator.runtimeAnimatorController = null;
		foreach (string name in new[] { "First", "Second" })
		{
			var child = new GameObject(name);
			child.transform.SetParent(root.transform);
			child.AddComponent<WiperController>();
		}
		string[] assetsBefore = AssetDatabase.FindAssets("", new[] { AssetRoot + "/Animations" });
		LogAssert.Expect(LogType.Warning, "[Sonata] Multiple WiperControllers found. Assign Wiper Animator manually on the intended controller.");

		Assert.IsTrue(EditorApplication.ExecuteMenuItem("Tools/Sonata URP/Create Wiper Animation for Selected Model"));

		Assert.IsNull(animator.runtimeAnimatorController);
		CollectionAssert.AreEquivalent(assetsBefore, AssetDatabase.FindAssets("", new[] { AssetRoot + "/Animations" }));
	}

}
