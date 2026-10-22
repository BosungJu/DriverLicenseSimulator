using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SonataReference.Editor
{
    public static class SonataUrpBuilder
    {
        public const string AssetRoot = "Assets/SonataURP";
        public const string PrefabPath = AssetRoot + "/Prefabs/Sonata_URP.prefab";
        private const string ModelPath = AssetRoot + "/Models/SonataURP.fbx";
        private const string ManifestPath = AssetRoot + "/Models/material_manifest.json";

        [Serializable]
        private sealed class Manifest
        {
            public MaterialDefinition[] materials;
        }

        [Serializable]
        private sealed class MaterialDefinition
        {
            public string name;
            public float[] baseColor;
            public float metallic;
            public float smoothness;
            public bool transparent;
            public float[] emissionColor;
            public string texture;
            public string normalTexture;
            public float normalStrength;
            public float coat;
            public float coatSmoothness;
        }

        [MenuItem("Tools/Sonata URP/Rebuild Materials and Prefab")]
        public static void BuildAssets()
        {
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
            {
                throw new InvalidOperationException("Install URP before building the Sonata materials.");
            }

            EnsureFolder(AssetRoot + "/Materials");
            EnsureFolder(AssetRoot + "/Prefabs");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            TextAsset manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
            if (manifestAsset == null)
            {
                throw new FileNotFoundException("Sonata material manifest is missing.", ManifestPath);
            }

            Manifest manifest = JsonUtility.FromJson<Manifest>(manifestAsset.text);
            Dictionary<string, Material> materials = new Dictionary<string, Material>();
            foreach (MaterialDefinition definition in manifest.materials)
            {
                materials.Add(definition.name, BuildMaterial(definition, litShader));
            }

            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Sonata FBX is missing.", ModelPath);
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.preserveHierarchy = true;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.isReadable = false;
            importer.addCollider = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (KeyValuePair<string, Material> entry in materials)
            {
                AssetImporter.SourceAssetIdentifier identifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), entry.Key);
                importer.AddRemap(identifier, entry.Value);
            }

            importer.SaveAndReimport();
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            GameObject root = new GameObject("Sonata_URP");
            try
            {
                GameObject model = PrefabUtility.InstantiatePrefab(source) as GameObject;
                if (model == null)
                {
                    throw new InvalidOperationException("Unable to instantiate the Sonata model.");
                }

                model.transform.SetParent(root.transform, false);
                foreach (LODGroup importedGroup in model.GetComponentsInChildren<LODGroup>(true))
                {
                    UnityEngine.Object.DestroyImmediate(importedGroup);
                }

                Transform frontLeft = FindTransform(model.transform, "Wheel_Front_Left");
                Transform frontRight = FindTransform(model.transform, "Wheel_Front_Right");
                Transform rearLeft = FindTransform(model.transform, "Wheel_Rear_Left");
                Transform rearRight = FindTransform(model.transform, "Wheel_Rear_Right");
                Vector3 front = (frontLeft.position + frontRight.position) * 0.5f;
                Vector3 rear = (rearLeft.position + rearRight.position) * 0.5f;
                Vector3 forward = front - rear;
                if (forward.sqrMagnitude < 1f)
                {
                    throw new InvalidOperationException("Wheel pivots do not define a valid vehicle direction.");
                }

                model.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.up)) * model.transform.localRotation;
                Bounds bounds = GetBounds(model.GetComponentsInChildren<Renderer>(true));
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] assigned = renderer.sharedMaterials;
                    for (int index = 0; index < assigned.Length; index++)
                    {
                        if (assigned[index] == null || !materials.TryGetValue(assigned[index].name, out Material replacement))
                        {
                            throw new InvalidOperationException("Unmapped material on " + renderer.name);
                        }

                        assigned[index] = replacement;
                    }

                    renderer.sharedMaterials = assigned;
                    if (assigned.Any(material => material.GetFloat("_Surface") > 0.5f))
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                    }
                }

                LODGroup lodGroup = root.AddComponent<LODGroup>();
                float[] transitions = { 0.55f, 0.22f, 0.015f };
                LOD[] lods = new LOD[transitions.Length];
                for (int index = 0; index < lods.Length; index++)
                {
                    Transform level = FindTransform(model.transform, "LOD" + index);
                    lods[index] = new LOD(transitions[index], level.GetComponentsInChildren<Renderer>(true));
                }

                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 0.73f, 0f);
                collider.size = new Vector3(1.86f, 1.30f, 4.90f);

                Transform driverSeat = FindTransform(model.transform, "Seat_Driver");
                Bounds driverBounds = GetBounds(driverSeat.GetComponentsInChildren<Renderer>(true));
                GameObject driverView = new GameObject("DriverView");
                driverView.transform.SetParent(root.transform, false);
                driverView.transform.localPosition = new Vector3(driverBounds.center.x, 1.13f, driverBounds.center.z + 0.15f);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Sonata URP] Materials and prefab created: " + PrefabPath);
        }

        private static Material BuildMaterial(MaterialDefinition definition, Shader litShader)
        {
            string path = AssetRoot + "/Materials/" + definition.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = definition.coat > 0f ? Shader.Find("Universal Render Pipeline/Complex Lit") : litShader;
            if (shader == null)
            {
                shader = litShader;
            }

            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.name = definition.name;
            material.shaderKeywords = Array.Empty<string>();
            material.SetColor("_BaseColor", ReadColor(definition.baseColor));
            material.SetFloat("_Metallic", definition.metallic);
            material.SetFloat("_Smoothness", definition.smoothness);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_Surface", definition.transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_SrcBlend", definition.transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            material.SetFloat("_DstBlend", definition.transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", definition.transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat("_ZWrite", definition.transparent ? 0f : 1f);
            material.SetOverrideTag("RenderType", definition.transparent ? "Transparent" : "Opaque");
            material.renderQueue = definition.transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            if (definition.transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            material.SetShaderPassEnabled("ShadowCaster", !definition.transparent);
            material.SetShaderPassEnabled("DepthOnly", !definition.transparent);
            material.SetShaderPassEnabled("DepthNormals", !definition.transparent);
            material.SetShaderPassEnabled("DepthNormalsOnly", !definition.transparent);
            if (material.HasProperty("_ClearCoatMask"))
            {
                material.SetFloat("_ClearCoat", definition.coat > 0f ? 1f : 0f);
                material.SetFloat("_ClearCoatMask", definition.coat);
                material.SetFloat("_ClearCoatSmoothness", definition.coatSmoothness);
                if (definition.coat > 0f)
                {
                    material.EnableKeyword("_CLEARCOAT");
                }
            }

            Texture2D baseTexture = LoadTexture(definition.texture, false);
            material.SetTexture("_BaseMap", baseTexture);
            Texture2D normalTexture = LoadTexture(definition.normalTexture, true);
            material.SetTexture("_BumpMap", normalTexture);
            if (normalTexture != null)
            {
                material.SetTextureScale("_BaseMap", Vector2.one * 8f);
                material.SetFloat("_BumpScale", definition.normalStrength);
                material.EnableKeyword("_NORMALMAP");
            }
            else
            {
                material.SetTextureScale("_BaseMap", Vector2.one);
            }

            Color emission = ReadColor(definition.emissionColor);
            if (baseTexture != null)
            {
                // The screen texture already contains its own colors.
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_EmissionMap", baseTexture);
                emission = Color.white * 0.5f;
            }
            else
            {
                material.SetTexture("_EmissionMap", null);
            }

            material.SetColor("_EmissionColor", emission);
            if (emission.maxColorComponent > 0.001f)
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D LoadTexture(string name, bool normalMap)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            string path = AssetRoot + "/Textures/" + name;
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                throw new FileNotFoundException("Missing Sonata texture.", path);
            }

            TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            TextureWrapMode wrapMode = normalMap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (importer.textureType != type || importer.sRGBTexture == normalMap || importer.wrapMode != wrapMode)
            {
                importer.textureType = type;
                importer.sRGBTexture = !normalMap;
                importer.mipmapEnabled = true;
                importer.wrapMode = wrapMode;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Color ReadColor(float[] components)
        {
            return new Color(components[0], components[1], components[2], components.Length > 3 ? components[3] : 1f);
        }

        public static Transform FindTransform(Transform root, string name)
        {
            Transform matching = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);
            if (matching == null)
            {
                throw new InvalidOperationException("Required Sonata group is missing: " + name);
            }

            return matching;
        }

        public static Bounds GetBounds(Renderer[] renderers)
        {
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException("The Sonata group has no renderers.");
            }

            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers.Skip(1))
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
