using System;
using UnityEditor;
using UnityEngine;

namespace SonataReference.Editor
{
	public static class SonataImprovedSetup
	{
		private const string AssetRoot = "Assets/Resources/Car/BasicModel/SonataURP";
		private const string ImprovedPrefabPath = AssetRoot + "/Prefabs/Sonata_URP_Improved.prefab";

		[MenuItem("Tools/Sonata URP/Create Improved Model Prefab")]
		public static void CreateImprovedPrefab()
		{
			GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Prefabs/Sonata_URP.prefab");
			GameObject improvedModel = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/SonataURP_Improved.fbx");
			GameObject sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/Models/SonataURP.fbx");
			if (original == null || improvedModel == null || sourceModel == null)
			{
				throw new InvalidOperationException("Sonata source prefab or improved FBX is missing. Wait for asset import to finish.");
			}

			if (AssetDatabase.LoadAssetAtPath<GameObject>(ImprovedPrefabPath) != null)
			{
				Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(ImprovedPrefabPath);
				Debug.Log("[Sonata] Improved prefab already exists; preserving existing edits.");
				return;
			}

			GameObject root = PrefabUtility.InstantiatePrefab(original) as GameObject;
			try
			{
				if (root == null)
				{
					throw new InvalidOperationException("Unable to instantiate Sonata prefab.");
				}

				PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
				root.name = "Sonata_URP_Improved";
				Transform oldModel = null;
				foreach (Transform child in root.transform)
				{
					if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == sourceModel)
					{
						oldModel = child;
						break;
					}
				}

				if (oldModel == null)
				{
					throw new InvalidOperationException("Original Sonata model child was not found.");
				}

				GameObject model = PrefabUtility.InstantiatePrefab(improvedModel) as GameObject;
				if (model == null)
				{
					throw new InvalidOperationException("Unable to instantiate improved Sonata FBX.");
				}

				model.transform.SetParent(root.transform, false);
				// The improved FBX already has Y-up, Z-forward axes and a ground-level origin.
				model.transform.localPosition = Vector3.zero;
				model.transform.localRotation = Quaternion.identity;
				model.transform.localScale = Vector3.one;
				PreserveModelOverrides(sourceModel.transform, oldModel, model.transform);
				UnityEngine.Object.DestroyImmediate(oldModel.gameObject);
				foreach (LODGroup importedGroup in model.GetComponentsInChildren<LODGroup>(true))
				{
					UnityEngine.Object.DestroyImmediate(importedGroup);
				}

				LODGroup group = root.GetComponent<LODGroup>();
				LOD[] lods = group != null ? group.GetLODs() : new LOD[0];
				for (int index = 0; index < lods.Length; index++)
				{
					Transform level = Array.Find(model.GetComponentsInChildren<Transform>(true), candidate => candidate.name == "LOD" + index);
					if (level == null)
					{
						throw new InvalidOperationException("Improved Sonata is missing LOD" + index);
					}

					lods[index].renderers = level.GetComponentsInChildren<Renderer>(true);
				}

				if (group != null)
				{
					group.SetLODs(lods);
					group.RecalculateBounds();
				}
				Selection.activeObject = PrefabUtility.SaveAsPrefabAsset(root, ImprovedPrefabPath);
			}
			finally
			{
				if (root != null)
				{
					UnityEngine.Object.DestroyImmediate(root);
				}
			}
		}

		private static void PreserveModelOverrides(Transform source, Transform original, Transform improved)
		{
			improved.gameObject.SetActive(original.gameObject.activeSelf);
			foreach (Transform candidate in improved.GetComponentsInChildren<Transform>(true))
			{
				if (candidate == null || candidate == improved)
				{
					continue;
				}

				string path = AnimationUtility.CalculateTransformPath(candidate, improved);
				Transform previous = original.Find(path);
				if (previous == null)
				{
					if (source.Find(path) != null)
					{
						UnityEngine.Object.DestroyImmediate(candidate.gameObject);
					}
					continue;
				}

				candidate.gameObject.SetActive(previous.gameObject.activeSelf);
				Renderer previousRenderer = previous.GetComponent<Renderer>();
				Renderer improvedRenderer = candidate.GetComponent<Renderer>();
				if (previousRenderer != null && improvedRenderer != null)
				{
					improvedRenderer.enabled = previousRenderer.enabled;
					improvedRenderer.shadowCastingMode = previousRenderer.shadowCastingMode;
					improvedRenderer.receiveShadows = previousRenderer.receiveShadows;
					Material[] previousMaterials = previousRenderer.sharedMaterials;
					Material[] improvedMaterials = improvedRenderer.sharedMaterials;
					bool hasSameMaterials = previousMaterials.Length == improvedMaterials.Length;
					for (int index = 0; hasSameMaterials && index < previousMaterials.Length; index++)
					{
						hasSameMaterials = previousMaterials[index] != null && improvedMaterials[index] != null
							&& previousMaterials[index].name == improvedMaterials[index].name;
					}

					if (hasSameMaterials)
					{
						improvedRenderer.sharedMaterials = previousMaterials;
					}
				}
			}
		}

		[MenuItem("Tools/Sonata URP/Add Dashboard to Selected Vehicle")]
		private static void AddDashboard()
		{
			GameObject selected = Selection.activeGameObject;
			CarController controller = selected != null ? selected.GetComponentInParent<CarController>() : null;
			if (controller == null || controller.GetComponent<VehicleSystems>() == null || controller.GetComponent<Rigidbody>() == null)
			{
				Debug.LogWarning("[Sonata] Select a configured vehicle with CarController, VehicleSystems and Rigidbody.");
				return;
			}

			if (controller.GetComponent<VehicleDashboardHud>() == null)
			{
				Undo.AddComponent<VehicleDashboardHud>(controller.gameObject);
			}
		}
	}
}
