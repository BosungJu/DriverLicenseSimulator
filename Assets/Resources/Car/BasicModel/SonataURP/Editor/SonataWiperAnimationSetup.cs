using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SonataReference.Editor
{
	public static class SonataWiperAnimationSetup
	{
		private const string AssetRoot = "Assets/Resources/Car/BasicModel/SonataURP/Animations";
		private const string SourcePath = AssetRoot + "/Animation_Wiper_Sweep.fbx";

		[MenuItem("Tools/Sonata URP/Create Wiper Animation for Selected Model")]
		private static void CreateAnimation()
		{
			if (EditorApplication.isPlaying)
			{
				Debug.LogWarning("[Sonata] Exit Play Mode before configuring wiper animation.");
				return;
			}

			GameObject model = Selection.activeGameObject;
			if (model == null || EditorUtility.IsPersistent(model))
			{
				Debug.LogWarning("[Sonata] Select the configured Sonata model instance in the Hierarchy.");
				return;
			}

			Transform[] transforms = model.GetComponentsInChildren<Transform>(true);
			var targets = new Dictionary<string, Transform>();
			foreach (Transform candidate in transforms)
			{
				if (!candidate.name.StartsWith("WiperPivot_", StringComparison.Ordinal))
				{
					continue;
				}

				if (targets.ContainsKey(candidate.name))
				{
					Debug.LogWarning("[Sonata] Multiple models found. Select only one Sonata model root.");
					return;
				}

				targets.Add(candidate.name, candidate);
			}

			if (targets.Count == 0)
			{
				Debug.LogWarning("[Sonata] No separated wiper pivots found under the selection.");
				return;
			}

			Animator animator = model.GetComponent<Animator>();
			if (!TrySelectController(model, animator, targets, out WiperController controller))
			{
				return;
			}

			if (animator != null && animator.runtimeAnimatorController != null)
			{
				ConnectController(model, animator, controller);
				return;
			}

			AnimationClip source = Array.Find(AssetDatabase.LoadAllAssetsAtPath(SourcePath), asset => asset is AnimationClip && !asset.name.StartsWith("__preview__", StringComparison.Ordinal)) as AnimationClip;
			if (source == null)
			{
				Debug.LogWarning("[Sonata] Wiper animation has not imported yet. Wait for Unity asset import to finish.");
				return;
			}

			var clip = new AnimationClip { name = "Animation_Wiper_Sweep", frameRate = 30f };
			var animatedTargets = new HashSet<Transform>();
			foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
			{
				int separator = binding.path.LastIndexOf('/');
				string targetName = binding.path.Substring(separator + 1);
				bool isRotation = binding.propertyName.StartsWith("m_LocalRotation.", StringComparison.Ordinal)
					|| binding.propertyName.StartsWith("localEulerAngles", StringComparison.Ordinal);
				if (binding.type != typeof(Transform) || !isRotation || !targets.TryGetValue(targetName, out Transform target))
				{
					continue;
				}

				EditorCurveBinding mappedBinding = binding;
				mappedBinding.path = AnimationUtility.CalculateTransformPath(target, model.transform);
				AnimationUtility.SetEditorCurve(clip, mappedBinding, AnimationUtility.GetEditorCurve(source, binding));
				animatedTargets.Add(target);
			}

			if (animatedTargets.Count != targets.Count)
			{
				UnityEngine.Object.DestroyImmediate(clip);
				Debug.LogWarning("[Sonata] Some wiper animation paths did not match. No assets or components were created.");
				return;
			}

			clip.EnsureQuaternionContinuity();
			AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
			settings.loopTime = true;
			AnimationUtility.SetAnimationClipSettings(clip, settings);
			string clipPath = AssetDatabase.GenerateUniqueAssetPath(AssetRoot + "/Animation_Wiper_Sweep.anim");
			AssetDatabase.CreateAsset(clip, clipPath);
			string controllerPath = AssetDatabase.GenerateUniqueAssetPath(AssetRoot + "/Animator_Wiper_Sweep.controller");
			AnimatorController animatorController = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
			AnimatorState state = animatorController.layers[0].stateMachine.AddState("Wiper Sweep");
			state.motion = clip;
			animatorController.layers[0].stateMachine.defaultState = state;

			if (animator == null)
			{
				animator = Undo.AddComponent<Animator>(model);
			}

			Undo.RecordObject(animator, "Assign Sonata Wiper Animation");
			animator.runtimeAnimatorController = animatorController;
			animator.applyRootMotion = false;
			animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
			EditorUtility.SetDirty(animator);
			ConnectController(model, animator, controller);
			AssetDatabase.SaveAssets();
			Selection.activeObject = clip;
			Debug.Log($"[Sonata] Created wiper animation for {animatedTargets.Count} pivots. WiperController starts parked and toggles from its input action.", model);
		}

		private static void ConnectController(GameObject model, Animator animator, WiperController controller)
		{
			RuntimeAnimatorController runtimeController = animator.runtimeAnimatorController;
			if (runtimeController is AnimatorOverrideController overrides)
			{
				runtimeController = overrides.runtimeAnimatorController;
			}

			AnimatorController animationController = runtimeController as AnimatorController;
			if (animationController == null || animationController.layers.Length == 0
				|| !Array.Exists(animationController.layers[0].stateMachine.states, child => child.state.name == "Wiper Sweep"))
			{
				Debug.LogWarning("[Sonata] Existing Animator has no Wiper Sweep state. Preserving the existing setup.");
				return;
			}


			if (controller == null)
			{
				controller = Undo.AddComponent<WiperController>(model);
			}

			Undo.RecordObject(controller, "Connect Sonata Wiper Controller");
			var serializedController = new SerializedObject(controller);
			serializedController.FindProperty("wiperAnimator").objectReferenceValue = animator;
			serializedController.FindProperty("animatorStateName").stringValue = animationController.layers[0].name + ".Wiper Sweep";
			serializedController.ApplyModifiedProperties();
			controller.enabled = true;
			PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
			EditorUtility.SetDirty(controller);
			Debug.Log("[Sonata] Connected WiperController to Animator. Assign its Wiper Action if no input action was configured.", controller);
		}
		private static bool TrySelectController(GameObject model, Animator animator, Dictionary<string, Transform> targets, out WiperController controller)
		{
			controller = null;
			WiperController[] candidates = model.transform.root.GetComponentsInChildren<WiperController>(true);
			WiperController[] matchingControllers = Array.FindAll(candidates, candidate => (animator != null && candidate.WiperAnimator == animator)
				|| (candidate.WiperAnimator == null && (targets.ContainsValue(candidate.leftPivot) || targets.ContainsValue(candidate.rightPivot))));
			if (matchingControllers.Length > 1)
			{
				Debug.LogWarning("[Sonata] Multiple WiperControllers target these pivots. Resolve the duplicate controllers before connecting Animator.");
				return false;
			}

			controller = matchingControllers.Length == 1 ? matchingControllers[0] : model.GetComponentInParent<WiperController>();
			if (controller == null && candidates.Length == 1)
			{
				controller = candidates[0];
			}

			if (controller == null && candidates.Length > 1)
			{
				Debug.LogWarning("[Sonata] Multiple WiperControllers found. Assign Wiper Animator manually on the intended controller.");
				return false;
			}

			if (controller != null && controller.WiperAnimator != null && controller.WiperAnimator != animator)
			{
				Debug.LogWarning("[Sonata] The WiperController is assigned to another Animator. Preserving that connection.");
				return false;
			}

			return true;
		}

	}
}
