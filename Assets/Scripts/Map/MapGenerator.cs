using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class MapRoutePointEnterEvent : UnityEvent<int, GameObject>
{
}

[System.Serializable]
public class MapLayerEnterEvent : UnityEvent<string, GameObject>
{
}

[ExecuteAlways]
public class MapGenerator : MonoBehaviour
{
    private const float MinimumLineWidth = 0.01f;
	private const float MinimumHillBarrierWidth = 0.4f;
	private const float MinimumHillBarrierHeight = 1f;
    private const float FullAngle = 360f;
    internal const string LineLayerName = "Line";
	internal const string OutlineLayerName = "Outline";
	internal const string CurbLayerName = "Curb";
    private const string TestLayerName = "T_Test";
    private const string RoutePointLayerName = "Point";
    private const string GeneratedLineMeshName = "Generated Line Layer";
    private const string GeneratedTestMeshName = "Generated T_Test Layer";
    private const string GeneratedRoutePointsName = "Generated Route Points";
    private const string GeneratedRepairsName = "Generated Open End Repairs";
    private const string GeneratedAdditionalLinesName = "Generated Additional Lines";
    private const string GeneratedBasePlaneName = "Generated Base Plane";
    private const string GeneratedHillSurfaceName = "Generated Hill Surface";
	private const string GeneratedHillCurbsName = "Generated Hill Curbs";

    public string mapName;
    public Mesh CurrentMesh => mesh;
    public int RoutePointCount => routePointCount;
    public IReadOnlyList<MapRouteInstruction> RouteInstructions => routeInstructions;
    public IReadOnlyList<MapRouteSection> RouteSections => routeSections;
    public string RouteInstructionError { get; private set; } = string.Empty;
    public event System.Action MapGenerated;
    public UnityEvent<GameObject> OnLineCollisionEnter => onLineCollisionEnter;
    public UnityEvent<GameObject> OnTestCollisionEnter => onTestCollisionEnter;
    public MapRoutePointEnterEvent OnRoutePointEnter => onRoutePointEnter;
    public MapLayerEnterEvent OnLayerEnter => onLayerEnter;

    public void SubscribeLineCollision(UnityAction<GameObject> listener)
    {
        onLineCollisionEnter.AddListener(listener);
    }

    public void UnsubscribeLineCollision(UnityAction<GameObject> listener)
    {
        onLineCollisionEnter.RemoveListener(listener);
    }

    public void SubscribeTestCollision(UnityAction<GameObject> listener)
    {
        onTestCollisionEnter.AddListener(listener);
    }

    public void UnsubscribeTestCollision(UnityAction<GameObject> listener)
    {
        onTestCollisionEnter.RemoveListener(listener);
    }

    public void SubscribeRoutePoint(UnityAction<int, GameObject> listener)
    {
        onRoutePointEnter.AddListener(listener);
    }

    public void UnsubscribeRoutePoint(UnityAction<int, GameObject> listener)
    {
        onRoutePointEnter.RemoveListener(listener);
    }

    [Header("Corner Settings")]
    [SerializeField] private int circleSegments = 64; // 원호를 그릴 때 사용할 세그먼트 수

    [Header("CAD Settings")]
    [SerializeField] private float cadUnitScale = 0.001f;
    [SerializeField] private float defaultLineWidth = 0.2f;
	[SerializeField, Min(0.01f)] private float curbHeight = 0.15f;
	[SerializeField] private Material curbMaterial;
    [SerializeField] private bool centerMapOnOrigin = true;
    [SerializeField] private bool preferOriginCsv = true;
    [SerializeField] private bool skipAnnotationLayers = true;
    [SerializeField] private string annotationLayers = "Defpoints";

    [Header("Plane Settings")]
    [SerializeField] private bool generateBasePlane = true;
    [SerializeField] private float planePadding = 2f;
    [SerializeField] private float planeHeight = -0.02f;
    [SerializeField] private float lineHeightOffset = 0.03f;
    [SerializeField] private Material planeMaterial;
    [SerializeField] private Material lineMaterial;

    [Header("Layer Collision Events")]
    [SerializeField] private UnityEvent<GameObject> onLineCollisionEnter = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onTestCollisionEnter = new UnityEvent<GameObject>();
    [SerializeField] private MapLayerEnterEvent onLayerEnter = new MapLayerEnterEvent();

    [Header("Route Point Settings")]
    [SerializeField] private bool generateRoutePoints = true;
    [SerializeField] private float routePointWidth = 6f;
    [SerializeField] private float routePointHeight = 3f;
    [SerializeField] private float routePointDepth = 1f;
    [SerializeField] private MapRoutePointEnterEvent onRoutePointEnter = new MapRoutePointEnterEvent();

    [Header("Hill Settings")]
    [SerializeField] private bool generateHillSurface = true;
    [SerializeField] private bool importHillFromLegacyCsv = true;
    [SerializeField] private string legacyHillMapName = "MapData/옥천학원수정";
    [SerializeField] private string hillLayerName = "Hill";
    [SerializeField] private float hillHeight = 1.5f;
	[SerializeField] private bool useTestCourseHillProfile = true;
	[SerializeField, Min(0f)] private float hillCurbPadding = 0.4f;
    [SerializeField] private Material hillMaterial;
	[SerializeField] private Material hillCurbMaterial;
	[SerializeField, Min(MinimumHillBarrierWidth)] private float hillCurbWidth = MinimumHillBarrierWidth;
	[SerializeField, Min(MinimumHillBarrierHeight)]
	[Tooltip("Height above the road. Keep this taller than the vehicle bumper/body; verify vehicle collision at the intended speed.")]
	private float hillBarrierHeight = 1.5f;

    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private readonly List<Vector3> otherLayerVertices = new List<Vector3>();
    private readonly List<int> otherLayerTriangles = new List<int>();
    private readonly Dictionary<string, LayerMeshData> layerMeshes = new Dictionary<string, LayerMeshData>(System.StringComparer.OrdinalIgnoreCase);
    private readonly List<Vector2> reusablePolylinePoints = new List<Vector2>(4);
    private HashSet<string> annotationLayerSet;
    private Mesh mesh;
    private int routePointCount;
    private IReadOnlyList<MapRouteInstruction> routeInstructions = System.Array.Empty<MapRouteInstruction>();
    private IReadOnlyList<MapRouteSection> routeSections = System.Array.Empty<MapRouteSection>();
    private Vector2 cadOrigin;
	private bool isDrawingCurb;
	private List<Vector2> activeCurbUVs;
	private float curbTextureDistance;
	private readonly MapHillProfile hillProfile = new MapHillProfile();
	private readonly List<float> hillSegmentCuts = new List<float>();

    private sealed class LayerMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();
		public readonly List<Vector2> UVs = new List<Vector2>();
    }

    public void SetMap(string s)
    {
        mapName = ResolveMapName(s);
        SetMapData(CsvParaser.GetMapData(mapName));
    }

    public void SetMapFromCsvFile(string csvFilePath)
    {
        mapName = System.IO.Path.GetFileNameWithoutExtension(csvFilePath);
        SetMapData(CsvParaser.GetMapDataFromCsvFile(csvFilePath));
    }

    public void SetMapData(List<MapData> mapDataList)
    {
        ClearMap();
        annotationLayerSet = null;
		hillProfile.Clear();
        if (mapDataList == null || mapDataList.Count == 0)
        {
            MapGenerated?.Invoke();
            return;
        }

        cadOrigin = GetCadOrigin(mapDataList);
        var hillDataList = GetHillData(mapDataList);
		hillProfile.Build(hillDataList, cadUnitScale, hillHeight);
		if (useTestCourseHillProfile)
		{
			hillProfile.ApplyCourseDimensions(cadUnitScale);
		}

        if (generateBasePlane)
        {
            GenerateBasePlane(mapDataList, hillDataList);
        }

        if (generateHillSurface)
        {
            GenerateHillSurface();
        }

        foreach (var data in mapDataList)
        {
            SelectMeshBuffers(data.Layer);
            DrawMapEntity(data);
        }

        SelectMeshBuffers(null);
        ApplyToMesh();

        if (generateRoutePoints)
        {
            GenerateRoutePoints(mapDataList);
        }

        MapGenerated?.Invoke();
    }

    [Button]
    public void GenerateMap()
    {
        SetMap(mapName);
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        QueueMissingSensorMeshRestore();
    }

    private void OnValidate()
    {
        QueueMissingSensorMeshRestore();
    }

    private void OnDisable()
    {
        UnityEditor.EditorApplication.delayCall -= RestoreMissingSensorMeshes;
    }

    private void QueueMissingSensorMeshRestore()
    {
        // Defer hierarchy changes until Unity has finished loading/validating the scene.
        UnityEditor.EditorApplication.delayCall -= RestoreMissingSensorMeshes;
        UnityEditor.EditorApplication.delayCall += RestoreMissingSensorMeshes;
    }

    private void RestoreMissingSensorMeshes()
    {
        if (this == null || !isActiveAndEnabled || Application.isPlaying
            || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
            || UnityEditor.EditorUtility.IsPersistent(this)
            || !gameObject.scene.IsValid())
        {
            return;
        }

        if (transform.Find(GeneratedRepairsName) != null)
        {
            ClearGeneratedChild(GeneratedRepairsName);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        string resolvedMapName = ResolveMapName(mapName);
        List<MapData> mapDataList = CsvParaser.GetMapData(resolvedMapName);
        if (mapDataList.Count == 0 || HasGeneratedLayerMeshes(mapDataList))
        {
            return;
        }

        mapName = resolvedMapName;
        SetMapData(mapDataList);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

	private bool HasGeneratedLayerMeshes(List<MapData> mapDataList)
	{
		var checkedLayers = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		foreach (MapData data in mapDataList)
		{
			if (!ShouldDraw(data) || !checkedLayers.Add(data.Layer))
			{
				continue;
			}

			bool requiresCollider = !string.Equals(data.Layer, LineLayerName, System.StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(data.Layer, OutlineLayerName, System.StringComparison.OrdinalIgnoreCase);
			if (!HasSensorMesh(GetLayerChildName(data.Layer), requiresCollider))
			{
				return false;
			}
		}
		return true;
	}

    private bool HasSensorMesh(string childName, bool requiresCollider)
    {
        Transform child = transform.Find(childName);
        return child != null
            && child.TryGetComponent<MeshFilter>(out var meshFilter)
            && meshFilter.sharedMesh != null
            && meshFilter.sharedMesh.vertexCount > 0
            && child.TryGetComponent<MeshRenderer>(out var meshRenderer)
            && meshRenderer.enabled
            && meshRenderer.sharedMaterial != null
			&& (!requiresCollider || (child.TryGetComponent<MeshCollider>(out var meshCollider)
				&& meshCollider.sharedMesh == meshFilter.sharedMesh));
    }

    [Button]
    public void SaveGeneratedMeshAsset()
    {
        if (mesh == null)
        {
            GenerateMap();
        }

        if (mesh == null)
        {
            Debug.LogWarning("MapGenerator: no mesh data to save.");
            return;
        }

        string fileName = GetSaveFileBaseName("GeneratedMapMesh");
        string path = UnityEditor.EditorUtility.SaveFilePanelInProject(
            "Save Generated Map Mesh",
            $"{fileName}_Mesh",
            "asset",
            "Choose where to save the generated map mesh asset.");

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var lineCombineInstances = new List<CombineInstance>();
        if (mesh.vertexCount > 0)
        {
            lineCombineInstances.Add(new CombineInstance { mesh = mesh });
        }

		Mesh curbMesh = null;
        foreach (Transform child in transform)
        {
            if (child.TryGetComponent<MapLayerCollisionRelay>(out _)
                && child.TryGetComponent<MeshFilter>(out var layerFilter)
                && layerFilter.sharedMesh != null)
            {
				if (string.Equals(child.name, GetLayerChildName(CurbLayerName), System.StringComparison.OrdinalIgnoreCase))
				{
					curbMesh = layerFilter.sharedMesh;
					continue;
				}
                lineCombineInstances.Add(new CombineInstance { mesh = layerFilter.sharedMesh });
            }
        }

        // Lines stay merged in the first sub-mesh; ground surfaces get their own sub-meshes
        // so the saved asset can use separate materials for lines, plane, and hill.
        Mesh lineMesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        lineMesh.CombineMeshes(lineCombineInstances.ToArray(), true, false);

        var subMeshInstances = new List<CombineInstance>();
        var subMeshNames = new List<string>();
        AddSubMeshInstance(subMeshInstances, subMeshNames, lineMesh, "Lines");
        AddSubMeshInstance(subMeshInstances, subMeshNames, GetGeneratedChildMesh(GeneratedBasePlaneName), "Base Plane");
        AddSubMeshInstance(subMeshInstances, subMeshNames, GetGeneratedChildMesh(GeneratedHillSurfaceName), "Hill Surface");
		AddSubMeshInstance(subMeshInstances, subMeshNames, GetGeneratedChildMesh(GeneratedHillCurbsName), "Hill Curbs");
		AddSubMeshInstance(subMeshInstances, subMeshNames, curbMesh, "Curbs");

        Mesh meshAsset = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        meshAsset.CombineMeshes(subMeshInstances.ToArray(), false, false);
        meshAsset.name = $"{fileName}_Mesh";
        DestroyImmediate(lineMesh);

        path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(path);
        UnityEditor.AssetDatabase.CreateAsset(meshAsset, path);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
        Debug.Log($"MapGenerator: saved mesh asset to {path} (sub-meshes: {string.Join(", ", subMeshNames)})");
    }

    [Button]
    public void SaveGeneratedPrefab()
    {
        // The mesh field is not serialized and resets on domain reload, so existing children are
        // checked first; regenerating would discard a map loaded through SetMapFromCsvFile.
        List<Transform> meshChildren = GetGeneratedMeshChildren();
        if (meshChildren.Count == 0)
        {
            GenerateMap();
            meshChildren = GetGeneratedMeshChildren();
        }

        if (meshChildren.Count == 0)
        {
            Debug.LogWarning("MapGenerator: no generated meshes to save as prefab.");
            return;
        }

        string fileName = GetSaveFileBaseName("GeneratedMap");
        string prefabPath = UnityEditor.EditorUtility.SaveFilePanelInProject(
            "Save Generated Map Prefab",
            fileName,
            "prefab",
            "Choose where to save the generated map prefab.");

        if (string.IsNullOrEmpty(prefabPath))
        {
            return;
        }

        prefabPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(prefabPath);
        string prefabPathWithoutExtension = prefabPath.Substring(0, prefabPath.Length - ".prefab".Length);
        string meshAssetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath($"{prefabPathWithoutExtension}_Meshes.asset");

        // Generated meshes and fallback materials live only in memory, so they are copied into
        // a companion asset first; otherwise the prefab would keep missing references.
        var prefabRoot = new GameObject(fileName);
        try
        {
            var savedMaterials = new Dictionary<Material, Material>();
            bool hasMeshAsset = false;

            foreach (Transform sourceChild in meshChildren)
            {
                CopyGeneratedChildToPrefab(sourceChild, prefabRoot.transform, meshAssetPath, savedMaterials, ref hasMeshAsset);
            }

            UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath, out bool isPrefabSaved);
            if (!isPrefabSaved)
            {
                Debug.LogError($"MapGenerator: failed to save prefab to {prefabPath}");
                return;
            }
        }
        finally
        {
            DestroyImmediate(prefabRoot);
        }

        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"MapGenerator: saved prefab to {prefabPath} (meshes: {meshAssetPath}, parts: {meshChildren.Count})");
    }

    private List<Transform> GetGeneratedMeshChildren()
    {
        var meshChildren = new List<Transform>();
        foreach (Transform child in transform)
        {
            if (child.TryGetComponent<MeshFilter>(out var childFilter)
                && childFilter.sharedMesh != null
                && childFilter.sharedMesh.vertexCount > 0)
            {
                meshChildren.Add(child);
            }
        }

        return meshChildren;
    }

    private void CopyGeneratedChildToPrefab(
        Transform sourceChild,
        Transform prefabRoot,
        string meshAssetPath,
        Dictionary<Material, Material> savedMaterials,
        ref bool hasMeshAsset)
    {
        var prefabChild = new GameObject(sourceChild.name);
        prefabChild.layer = sourceChild.gameObject.layer;
        prefabChild.transform.SetParent(prefabRoot, false);
        prefabChild.transform.localPosition = sourceChild.localPosition;
        prefabChild.transform.localRotation = sourceChild.localRotation;
        prefabChild.transform.localScale = sourceChild.localScale;

        Mesh sourceMesh = sourceChild.GetComponent<MeshFilter>().sharedMesh;
        Mesh savedMesh = Instantiate(sourceMesh);
        savedMesh.name = sourceMesh.name;
        SaveToMeshAsset(savedMesh, meshAssetPath, ref hasMeshAsset);
        prefabChild.AddComponent<MeshFilter>().sharedMesh = savedMesh;

        if (sourceChild.TryGetComponent<MeshRenderer>(out var sourceRenderer))
        {
            Material[] sourceMaterials = sourceRenderer.sharedMaterials;
            var prefabMaterials = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                prefabMaterials[i] = GetPersistentMaterial(sourceMaterials[i], meshAssetPath, savedMaterials, ref hasMeshAsset);
            }

            prefabChild.AddComponent<MeshRenderer>().sharedMaterials = prefabMaterials;
        }

        if (sourceChild.TryGetComponent<MeshCollider>(out _))
        {
            prefabChild.AddComponent<MeshCollider>().sharedMesh = savedMesh;
        }
    }

    private Material GetPersistentMaterial(
        Material sourceMaterial,
        string meshAssetPath,
        Dictionary<Material, Material> savedMaterials,
        ref bool hasMeshAsset)
    {
        if (sourceMaterial == null || UnityEditor.EditorUtility.IsPersistent(sourceMaterial))
        {
            return sourceMaterial;
        }

        if (savedMaterials.TryGetValue(sourceMaterial, out Material savedMaterial))
        {
            return savedMaterial;
        }

        savedMaterial = new Material(sourceMaterial) { name = sourceMaterial.name };
        SaveToMeshAsset(savedMaterial, meshAssetPath, ref hasMeshAsset);
        savedMaterials.Add(sourceMaterial, savedMaterial);
        return savedMaterial;
    }

    private static void SaveToMeshAsset(Object assetObject, string meshAssetPath, ref bool hasMeshAsset)
    {
        // The first object becomes the main asset; the rest are stored as its sub-assets.
        if (hasMeshAsset)
        {
            UnityEditor.AssetDatabase.AddObjectToAsset(assetObject, meshAssetPath);
            return;
        }

        UnityEditor.AssetDatabase.CreateAsset(assetObject, meshAssetPath);
        hasMeshAsset = true;
    }

    private string GetSaveFileBaseName(string fallbackName)
    {
        return string.IsNullOrWhiteSpace(mapName)
            ? fallbackName
            : System.IO.Path.GetFileNameWithoutExtension(mapName);
    }

    private Mesh GetGeneratedChildMesh(string childName)
    {
        Transform child = transform.Find(childName);
        if (child == null || !child.TryGetComponent<MeshFilter>(out var meshFilter))
        {
            return null;
        }

        return meshFilter.sharedMesh;
    }

    private static void AddSubMeshInstance(List<CombineInstance> subMeshInstances, List<string> subMeshNames, Mesh sourceMesh, string subMeshName)
    {
        if (sourceMesh == null || sourceMesh.vertexCount == 0)
        {
            return;
        }

        subMeshInstances.Add(new CombineInstance { mesh = sourceMesh });
        subMeshNames.Add(subMeshName);
    }
#endif

    public void DrawLine(Vector2 start, Vector2 end, float width)
    {
        if ((end - start).sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

		float padding = GetHillPadding(width);
		hillProfile.GetSegmentCuts(start, end, padding, hillSegmentCuts);
		for (int i = 0; i < hillSegmentCuts.Count - 1; i++)
		{
			DrawLineSegment(Vector2.Lerp(start, end, hillSegmentCuts[i]),
				Vector2.Lerp(start, end, hillSegmentCuts[i + 1]), width, padding);
		}
	}

	private float GetHillPadding(float width)
	{
		return isDrawingCurb && cadUnitScale > 0f ? Mathf.Max(hillCurbPadding, width * 0.5f) / cadUnitScale : 0f;
	}

	private void DrawLineSegment(Vector2 start, Vector2 end, float width, float padding)
	{
		const float minimumSegmentLengthMeters = 0.001f;
		if ((end - start).magnitude * cadUnitScale < minimumSegmentLengthMeters)
		{
			return;
		}
		hillProfile.GetSegmentHeights(start, end, padding, out float startHeight, out float endHeight);
		Vector3 s = ToGroundPoint(start);
		Vector3 e = ToGroundPoint(end);
		s.y = startHeight + (isDrawingCurb ? 0f : lineHeightOffset);
		e.y = endHeight + (isDrawingCurb ? 0f : lineHeightOffset);
		if (isDrawingCurb)
		{
			DrawCurbSegment(s, e, width, curbHeight, vertices, triangles, 0f, activeCurbUVs, curbTextureDistance);
			curbTextureDistance += Vector3.Distance(s, e);
			return;
		}

        Vector3 forward = (e - s).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward).normalized * (width * 0.5f);

        int baseIdx = vertices.Count;

        // 사각형의 네 꼭짓점 (XZ 평면)
        vertices.Add(s - side); // v0
        vertices.Add(s + side); // v1
        vertices.Add(e - side); // v2
        vertices.Add(e + side); // v3

        // 시계 방향 인덱싱
        triangles.Add(baseIdx); triangles.Add(baseIdx + 2); triangles.Add(baseIdx + 1);
        triangles.Add(baseIdx + 2); triangles.Add(baseIdx + 3); triangles.Add(baseIdx + 1);
    }

	private void DrawCurbSegment(Vector3 start, Vector3 end, float width, float rise,
		List<Vector3> meshVertices, List<int> meshTriangles, float bottomHeight = 0f,
		List<Vector2> meshUVs = null, float textureStart = 0f)
	{
		float textureEnd = textureStart + Vector3.Distance(start, end);
		float bottomStartV = bottomHeight - start.y;
		float bottomEndV = bottomHeight - end.y;
		Vector3 direction = end - start;
		direction.y = 0f;
		direction.Normalize();
		Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * (width * 0.5f);
		Vector3 height = Vector3.up * Mathf.Max(rise, MinimumLineWidth);
		// Overlap adjacent segments to close small notches at curve and corner joints.
		start -= direction * (width * 0.5f);
		end += direction * (width * 0.5f);
		Vector3 bottomStart = start;
		Vector3 bottomEnd = end;
		// Retaining sides reach the flat ground beneath the hill rather than hanging above it.
		bottomStart.y = bottomHeight;
		bottomEnd.y = bottomHeight;
		Vector3[] corners =
		{
			bottomStart - side, bottomStart + side, bottomEnd - side, bottomEnd + side,
			start - side + height, start + side + height, end - side + height, end + side + height
		};
		// Independent face vertices keep the curb's edges sharp after normal calculation.
		int[] faceCorners = { 0, 1, 2, 3, 4, 6, 5, 7, 0, 2, 4, 6, 1, 5, 3, 7, 0, 4, 1, 5, 2, 3, 6, 7 };
		float u0 = textureStart - width * 0.5f;
		float u1 = textureEnd + width * 0.5f;
		float vTop = Mathf.Max(rise, MinimumLineWidth);
		Vector2[] faceUVs =
		{
			new Vector2(u0, 0f), new Vector2(u0, width), new Vector2(u1, 0f), new Vector2(u1, width),
			new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u0, width), new Vector2(u1, width),
			new Vector2(u0, bottomStartV), new Vector2(u1, bottomEndV), new Vector2(u0, vTop), new Vector2(u1, vTop),
			new Vector2(u0, bottomStartV), new Vector2(u0, vTop), new Vector2(u1, bottomEndV), new Vector2(u1, vTop),
			new Vector2(0f, bottomStartV), new Vector2(0f, vTop), new Vector2(width, bottomStartV), new Vector2(width, vTop),
			new Vector2(0f, bottomEndV), new Vector2(width, bottomEndV), new Vector2(0f, vTop), new Vector2(width, vTop)
		};
		for (int face = 0; face < faceCorners.Length; face += 4)
		{
			int firstVertex = meshVertices.Count;
			for (int corner = 0; corner < 4; corner++)
			{
				meshVertices.Add(corners[faceCorners[face + corner]]);
				if (meshUVs != null)
				{
					meshUVs.Add(faceUVs[face + corner]);
				}
			}
			meshTriangles.Add(firstVertex);
			meshTriangles.Add(firstVertex + 1);
			meshTriangles.Add(firstVertex + 2);
			meshTriangles.Add(firstVertex + 2);
			meshTriangles.Add(firstVertex + 1);
			meshTriangles.Add(firstVertex + 3);
		}
	}

    public void DrawPolyline(Vector2[] points, float width, bool isClosed)
    {
        if (points.Length < 2) return;

        DrawPolylineSegments(points, points.Length, width, isClosed);
    }

    public void DrawCircle(Vector2 center, float radius, float width)
    {
        if (radius <= Mathf.Epsilon)
        {
            return;
        }

        float angleStep = 360f / circleSegments;
        Vector2[] points = new Vector2[circleSegments];

        for (int i = 0; i < circleSegments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        DrawPolyline(points, width, true);
    }

    public void DrawArc(Vector2 center, float radius, float startAngle, float endAngle, float width)
    {
        if (radius <= Mathf.Epsilon)
        {
            return;
        }

        float angleRange = endAngle - startAngle;
        int segmentCount = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(angleRange) / 360f * circleSegments));
        float angleStep = angleRange / segmentCount;
        Vector2[] points = new Vector2[segmentCount + 1];

        for (int i = 0; i <= segmentCount; i++)
        {
            float angle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
            points[i] = new Vector2(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius
            );
        }
        DrawPolyline(points, width, false);
    }

    public void DrawEllipse(Vector2 center, Vector2 majorVector, Vector2 minorVector, float majorRadius, float minorRadius, float width)
    {
        DrawEllipse(center, majorVector, minorVector, majorRadius, minorRadius, FullAngle, 0f, width);
    }

    public void DrawEllipse(Vector2 center, Vector2 majorVector, Vector2 minorVector, float majorRadius, float minorRadius, float totalAngle, float startAngle, float width)
    {
        if (majorRadius <= Mathf.Epsilon || minorRadius <= Mathf.Epsilon)
        {
            return;
        }

        Vector2 majorAxis = majorVector.sqrMagnitude > Mathf.Epsilon ? majorVector.normalized * majorRadius : Vector2.right * majorRadius;
        Vector2 minorAxis = minorVector.sqrMagnitude > Mathf.Epsilon ? minorVector.normalized * minorRadius : Vector2.up * minorRadius;
        int segmentCount = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(totalAngle) / FullAngle * circleSegments));
        bool isClosed = Mathf.Abs(Mathf.Abs(totalAngle) - FullAngle) <= 0.1f;
        Vector2[] points = new Vector2[isClosed ? segmentCount : segmentCount + 1];

        for (int i = 0; i < points.Length; i++)
        {
            float t = isClosed ? (float)i / segmentCount : (float)i / (points.Length - 1);
            float angle = (startAngle + totalAngle * t) * Mathf.Deg2Rad;
            points[i] = center + majorAxis * Mathf.Cos(angle) + minorAxis * Mathf.Sin(angle);
        }

        DrawPolyline(points, width, isClosed);
    }

    public void DrawHill(Vector2[] points, float width)
    {
        // TODO 선형 보간을 이용해 곡선 형태로 그리기
        DrawPolyline(points, width, false);
    }

    private void DrawPolylineSegments(IReadOnlyList<Vector2> points, int count, float width, bool isClosed)
    {
        for (int i = 0; i < count - 1; i++)
        {
            DrawLine(points[i], points[i + 1], width);
        }

        if (isClosed)
        {
            DrawLine(points[count - 1], points[0], width);
        }
    }

    public void ApplyToMesh()
    {
        ClearLegacyRootMesh();
        ClearGeneratedChild(GeneratedRepairsName);
        ClearGeneratedChild(GeneratedAdditionalLinesName);

        if (mesh != null)
        {
            DestroyMesh(mesh);
        }

        mesh = new Mesh { name = "Generated Additional Lines Mesh" };
        if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (mesh.vertexCount > 0)
        {
            var additionalLines = new GameObject(GeneratedAdditionalLinesName);
            additionalLines.transform.SetParent(transform, false);
            additionalLines.AddComponent<MeshFilter>().sharedMesh = mesh;
            additionalLines.AddComponent<MeshRenderer>().sharedMaterial = lineMaterial != null
                ? lineMaterial
                : GetDefaultMaterial(GeneratedAdditionalLinesName);
        }

        foreach (var layer in layerMeshes)
        {
            string childName = GetLayerChildName(layer.Key);
            ClearGeneratedChild(childName);
            CreateLayerMesh(childName, $"{childName} Mesh", layer.Value.Vertices, layer.Value.Triangles, layer.Key, layer.Value.UVs);
        }

    }

    public void ClearMap()
    {
        ClearLegacyRootMesh();

        vertices.Clear();
        triangles.Clear();
        otherLayerVertices.Clear();
        otherLayerTriangles.Clear();
        // Serialized relays also identify generated layers after a scene reload.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.TryGetComponent<MapLayerCollisionRelay>(out _))
            {
                ClearGeneratedChild(child.name);
            }
        }
        layerMeshes.Clear();
        routePointCount = 0;
        routeSections = System.Array.Empty<MapRouteSection>();
        routeInstructions = System.Array.Empty<MapRouteInstruction>();
        RouteInstructionError = string.Empty;
        vertices = otherLayerVertices;
        triangles = otherLayerTriangles;
		isDrawingCurb = false;
		activeCurbUVs = null;
		curbTextureDistance = 0f;
        ClearGeneratedChild(GeneratedBasePlaneName);
        ClearGeneratedChild(GeneratedHillSurfaceName);
		ClearGeneratedChild(GeneratedHillCurbsName);
        ClearGeneratedChild(GeneratedLineMeshName);
        ClearGeneratedChild(GeneratedTestMeshName);
        ClearGeneratedChild(GeneratedRoutePointsName);
        ClearGeneratedChild(GeneratedRepairsName);
        ClearGeneratedChild(GeneratedAdditionalLinesName);

        if (mesh != null)
        {
            DestroyMesh(mesh);
            mesh = null;
        }

    }

    private void SelectMeshBuffers(string layerName)
    {
		isDrawingCurb = string.Equals(layerName, CurbLayerName, System.StringComparison.OrdinalIgnoreCase);
        if (layerName == null)
        {
            vertices = otherLayerVertices;
            triangles = otherLayerTriangles;
			activeCurbUVs = null;
            return;
        }

        if (!layerMeshes.TryGetValue(layerName, out LayerMeshData layerData))
        {
            layerData = new LayerMeshData();
            layerMeshes.Add(layerName, layerData);
        }

        vertices = layerData.Vertices;
        triangles = layerData.Triangles;
		activeCurbUVs = isDrawingCurb ? layerData.UVs : null;
    }

    private string GetLayerChildName(string layerName)
    {
        if (string.Equals(layerName, LineLayerName, System.StringComparison.OrdinalIgnoreCase))
        {
            return GeneratedLineMeshName;
        }

        if (string.Equals(layerName, TestLayerName, System.StringComparison.OrdinalIgnoreCase))
        {
            return GeneratedTestMeshName;
        }

        return $"Generated {layerName} Layer";
    }

    private void CreateLayerMesh(
        string childName,
        string meshName,
        List<Vector3> meshVertices,
        List<int> meshTriangles,
        string layerName, List<Vector2> meshUVs)
    {
		bool isOutline = string.Equals(layerName, OutlineLayerName, System.StringComparison.OrdinalIgnoreCase);
		bool hasCollision = !isOutline && !string.Equals(layerName, LineLayerName, System.StringComparison.OrdinalIgnoreCase);
		Material layerMaterial = lineMaterial;
		if (isOutline)
		{
			layerMaterial = lineMaterial != null ? new Material(lineMaterial) : GetDefaultMaterial(childName);
			layerMaterial.name = $"{childName} Material";
			layerMaterial.color = Color.yellow;
		}
		else if (string.Equals(layerName, CurbLayerName, System.StringComparison.OrdinalIgnoreCase) && curbMaterial != null)
		{
			layerMaterial = curbMaterial;
		}
		GameObject child = CreateChildMesh(childName, meshName, meshVertices, meshTriangles, layerMaterial, hasCollision,
			string.Equals(layerName, CurbLayerName, System.StringComparison.OrdinalIgnoreCase) ? meshUVs : null);
        if (child == null)
        {
            return;
        }

        MapLayerCollisionRelay collisionRelay = child.AddComponent<MapLayerCollisionRelay>();
        collisionRelay.Initialize(this, layerName);
    }

    public void NotifyLayerCollision(string layerName, Collision collision)
    {
        GameObject otherObject = collision.rigidbody != null
            ? collision.rigidbody.gameObject
            : collision.gameObject;
        NotifyLayerEnter(layerName, otherObject);
    }

    private void NotifyLayerEnter(string layerName, GameObject otherObject)
    {
        onLayerEnter.Invoke(layerName, otherObject);
        if (string.Equals(layerName, LineLayerName, System.StringComparison.OrdinalIgnoreCase))
        {
            onLineCollisionEnter.Invoke(otherObject);
        }
        else if (string.Equals(layerName, TestLayerName, System.StringComparison.OrdinalIgnoreCase))
        {
            onTestCollisionEnter.Invoke(otherObject);
        }
    }

    public void NotifyLayerTrigger(string layerName, int routePointIndex, Collider other)
    {
        GameObject otherObject = other.attachedRigidbody != null
            ? other.attachedRigidbody.gameObject
            : other.gameObject;
        NotifyLayerEnter(layerName, otherObject);
        if (routePointIndex >= 0 && string.Equals(layerName, RoutePointLayerName, System.StringComparison.OrdinalIgnoreCase))
        {
            onRoutePointEnter.Invoke(routePointIndex, otherObject);
        }
    }

    private void GenerateRoutePoints(List<MapData> mapDataList)
    {
        var routeDataList = new List<MapData>();
        foreach (var data in mapDataList)
        {
            if (data.Name == "Point"
                && string.Equals(data.Layer, RoutePointLayerName, System.StringComparison.OrdinalIgnoreCase))
            {
                routeDataList.Add(data);
            }
        }

        // Point 레이어는 CAD Z 값을 시험 경로의 통과 순서로 사용한다.
        routeDataList.Sort((left, right) => left.PosZ.CompareTo(right.PosZ));
        routePointCount = routeDataList.Count;
        BuildRouteInstructions(mapDataList, routeDataList);
        if (routePointCount == 0)
        {
            return;
        }

        var routeParent = new GameObject(GeneratedRoutePointsName);
        routeParent.transform.SetParent(transform, false);
        var generatedSections = new List<MapRouteSection>(routePointCount);

        for (int i = 0; i < routeDataList.Count; i++)
        {
            MapData routeData = routeDataList[i];
            Vector2 cadPoint = new Vector2(routeData.PosX, routeData.PosY);
            Vector2 routeDirection = GetRouteDirection(routeDataList, i);
            Vector2 scaledPoint = (cadPoint - cadOrigin) * cadUnitScale;

            var routePoint = new GameObject($"Route Point {routeData.PosZ:00}");
            routePoint.transform.SetParent(routeParent.transform, false);
            routePoint.transform.localPosition = new Vector3(
                scaledPoint.x,
                GetGroundHeight(cadPoint) + Mathf.Max(routePointHeight, MinimumLineWidth) * 0.5f,
                scaledPoint.y);

            if (routeDirection.sqrMagnitude > Mathf.Epsilon)
            {
                routePoint.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(routeDirection.x, 0f, routeDirection.y),
                    Vector3.up);
            }

            var routeCollider = routePoint.AddComponent<BoxCollider>();
            routeCollider.isTrigger = true;
            routeCollider.size = new Vector3(
                Mathf.Max(routePointWidth, MinimumLineWidth),
                Mathf.Max(routePointHeight, MinimumLineWidth),
                Mathf.Max(routePointDepth, MinimumLineWidth));

            MapLayerCollisionRelay collisionRelay = routePoint.AddComponent<MapLayerCollisionRelay>();
            collisionRelay.Initialize(this, RoutePointLayerName, i);

            MapRouteInstruction instruction = i < routeInstructions.Count ? routeInstructions[i] : null;
            MapRouteSection section = routePoint.AddComponent<MapRouteSection>();
            section.Initialize(i, routeData, instruction);
            generatedSections.Add(section);
        }

        routeSections = generatedSections.AsReadOnly();
    }

    private void BuildRouteInstructions(List<MapData> mapDataList, List<MapData> sortedRoutePoints)
    {
        MapData instructionData = null;
        foreach (MapData data in mapDataList)
        {
            if (data.Name != "Text" || !string.Equals(data.Layer, "DATA", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (instructionData != null)
            {
                RouteInstructionError = "Expected one DATA Text containing the route instructions.";
                Debug.LogError($"MapGenerator: {RouteInstructionError}", this);
                return;
            }

            instructionData = data;
        }

        // Maps without DATA retain their existing route-only behavior.
        if (instructionData == null)
        {
            return;
        }

        if (!MapRouteInstruction.TryParse(instructionData.Text, sortedRoutePoints, out routeInstructions, out string error))
        {
            RouteInstructionError = error;
            Debug.LogError($"MapGenerator: {error}", this);
        }
    }

    private Vector2 GetRouteDirection(List<MapData> routeDataList, int routePointIndex)
    {
        int previousIndex = Mathf.Max(0, routePointIndex - 1);
        int nextIndex = Mathf.Min(routeDataList.Count - 1, routePointIndex + 1);
        Vector2 previousPoint = new Vector2(routeDataList[previousIndex].PosX, routeDataList[previousIndex].PosY);
        Vector2 nextPoint = new Vector2(routeDataList[nextIndex].PosX, routeDataList[nextIndex].PosY);
        return (nextPoint - previousPoint).normalized;
    }

    private void ClearLegacyRootMesh()
    {
        // Older scenes may still contain the former root mesh components.
        if (TryGetComponent<MeshFilter>(out var legacyFilter))
        {
            legacyFilter.sharedMesh = null;
        }

        if (TryGetComponent<MeshCollider>(out var legacyCollider))
        {
            legacyCollider.sharedMesh = null;
        }
    }

    private void ClearGeneratedChild(string childName)
    {
        var child = transform.Find(childName);
        if (child == null)
        {
            return;
        }

        if (child.TryGetComponent<MeshFilter>(out var meshFilter) && meshFilter.sharedMesh != null)
        {
            DestroyMesh(meshFilter.sharedMesh);
            meshFilter.sharedMesh = null;
        }

        if (child.TryGetComponent<MeshRenderer>(out var meshRenderer) && meshRenderer.sharedMaterial != null)
        {
            DestroyGeneratedMaterial(meshRenderer.sharedMaterial);
            meshRenderer.sharedMaterial = null;
        }

        if (Application.isPlaying)
        {
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
        else
        {
            DestroyImmediate(child.gameObject);
        }
    }

    private void DestroyGeneratedMaterial(Material targetMaterial)
    {
        if (targetMaterial == planeMaterial || targetMaterial == lineMaterial || targetMaterial == hillMaterial || targetMaterial == hillCurbMaterial || targetMaterial == curbMaterial)
        {
            return;
        }

#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.IsPersistent(targetMaterial))
        {
            return;
        }
#endif

        if (Application.isPlaying)
        {
            Destroy(targetMaterial);
        }
        else
        {
            DestroyImmediate(targetMaterial);
        }
    }

    private bool ShouldDraw(MapData data)
    {
        if (skipAnnotationLayers && IsAnnotationLayer(data.Layer))
        {
            return false;
        }

        return data.Name == "Line"
            || data.Name == "PolyLine"
            || data.Name == "Arc"
            || data.Name == "Ellipse"
            || data.Name == "Circle";
    }

    private void DrawMapEntity(MapData data)
    {
		curbTextureDistance = 0f;
        if (!ShouldDraw(data))
        {
            return;
        }

        float width = GetLineWidth(data);

        if (data.Name == "Line")
        {
            DrawLine(GetStartPoint(data), GetEndPoint(data), width);
        }
        else if (data.Name == "PolyLine")
        {
            DrawPolyline(data, width);
        }
        else if (data.Name == "Arc")
        {
            DrawArc(
                new Vector2(data.CentorPointX, data.CentorPointY),
                data.Radius,
                data.StartDegree,
                data.StartDegree + data.TotalAngle,
                width);
        }
        else if (data.Name == "Ellipse")
        {
            DrawEllipse(
                new Vector2(data.CentorPointX, data.CentorPointY),
                new Vector2(data.MajorVectorX, data.MajorVectorY),
                new Vector2(data.MinorVectorX, data.MinorVectorY),
                data.MajorRadius,
                data.MinorRadius,
                GetEllipseTotalAngle(data),
                data.StartDegree,
                width);
        }
        else if (data.Name == "Circle")
        {
            DrawCircle(new Vector2(data.CentorPointX, data.CentorPointY), data.Radius, width);
        }
    }

    private string ResolveMapName(string requestedMapName)
    {
        if (string.IsNullOrWhiteSpace(requestedMapName))
        {
            return CsvParaser.mapData;
        }

        if (!preferOriginCsv)
        {
            return requestedMapName;
        }

        string extension = System.IO.Path.GetExtension(requestedMapName);
        string pathWithoutExtension = string.IsNullOrEmpty(extension)
            ? requestedMapName
            : requestedMapName.Substring(0, requestedMapName.Length - extension.Length);

        if (pathWithoutExtension.EndsWith("_origin", System.StringComparison.OrdinalIgnoreCase)
            || pathWithoutExtension.EndsWith("_final", System.StringComparison.OrdinalIgnoreCase))
        {
            return requestedMapName;
        }

        return $"{pathWithoutExtension}_origin";
    }

    private List<MapData> GetHillData(List<MapData> mapDataList)
    {
        var hillDataList = new List<MapData>();
        AddHillData(mapDataList, hillDataList);

        if (hillDataList.Count == 0 && importHillFromLegacyCsv && !string.IsNullOrWhiteSpace(legacyHillMapName))
        {
            AddHillData(CsvParaser.GetMapData(legacyHillMapName), hillDataList);
        }

        return hillDataList;
    }

    private void AddHillData(List<MapData> source, List<MapData> destination)
    {
        if (source == null)
        {
            return;
        }

        foreach (var data in source)
        {
            if (IsHillData(data))
            {
                destination.Add(data);
            }
        }
    }

    private bool IsHillData(MapData data)
    {
        return data.Name == "Line"
            && data.Layer.Equals(hillLayerName, System.StringComparison.OrdinalIgnoreCase);
    }

    private void GenerateBasePlane(List<MapData> mapDataList, List<MapData> hillDataList)
    {
        if (!TryGetCadBounds(mapDataList, hillDataList, out var min, out var max))
        {
            return;
        }

        Vector3 worldMin = ToGroundPoint(min);
        Vector3 worldMax = ToGroundPoint(max);
        worldMin.x -= planePadding;
        worldMin.z -= planePadding;
        worldMax.x += planePadding;
        worldMax.z += planePadding;

        var planeVertices = new List<Vector3>
        {
            new Vector3(worldMin.x, planeHeight, worldMin.z),
            new Vector3(worldMin.x, planeHeight, worldMax.z),
            new Vector3(worldMax.x, planeHeight, worldMin.z),
            new Vector3(worldMax.x, planeHeight, worldMax.z)
        };
        var planeTriangles = new List<int> { 0, 1, 2, 2, 1, 3 };
        CreateChildMesh(GeneratedBasePlaneName, $"{GeneratedBasePlaneName} Mesh", planeVertices, planeTriangles, planeMaterial, true);
    }

    private void GenerateHillSurface()
    {
        if (!hillProfile.IsValid)
        {
            return;
        }

        var hillVertices = new List<Vector3>();
        var hillTriangles = new List<int>();

        for (int i = 0; i < hillProfile.Stations.Count; i++)
        {
            float height = hillProfile.GetStationHeight(i);
            Vector3 lower = ToGroundPoint(hillProfile.ToCadPoint(hillProfile.Stations[i], hillProfile.MinAcross));
            Vector3 upper = ToGroundPoint(hillProfile.ToCadPoint(hillProfile.Stations[i], hillProfile.MaxAcross));
            lower.y = height;
            upper.y = height;
            hillVertices.Add(lower);
            hillVertices.Add(upper);
        }

        for (int i = 0; i < hillProfile.Stations.Count - 1; i++)
        {
            int baseIdx = i * 2;
            hillTriangles.Add(baseIdx);
            hillTriangles.Add(baseIdx + 1);
            hillTriangles.Add(baseIdx + 2);
            hillTriangles.Add(baseIdx + 2);
            hillTriangles.Add(baseIdx + 1);
            hillTriangles.Add(baseIdx + 3);
        }

		// Separate vertices keep the side walls vertical without smoothing the road's edge normals.
		for (int i = 0; i < hillProfile.Stations.Count - 1; i++)
		{
			int baseIndex = i * 2;
			AddHillSideWall(hillVertices[baseIndex], hillVertices[baseIndex + 2], false, hillVertices, hillTriangles);
			AddHillSideWall(hillVertices[baseIndex + 1], hillVertices[baseIndex + 3], true, hillVertices, hillTriangles);
		}

        Material surfaceMaterial = hillMaterial != null ? hillMaterial : planeMaterial;
        CreateChildMesh(GeneratedHillSurfaceName, $"{GeneratedHillSurfaceName} Mesh", hillVertices, hillTriangles, surfaceMaterial, true);
		GenerateHillCurbs();
    }

	private void GenerateHillCurbs()
	{
		if (!hillProfile.IsValid || cadUnitScale <= 0f)
		{
			return;
		}
		var curbVertices = new List<Vector3>();
		var curbTriangles = new List<int>();
		var curbUVs = new List<Vector2>();
		float width = Mathf.Max(hillCurbWidth, MinimumHillBarrierWidth);
		float halfCadWidth = width * 0.5f / cadUnitScale;
		float[] sides = { hillProfile.MinAcross - halfCadWidth, hillProfile.MaxAcross + halfCadWidth };
		foreach (float lateral in sides)
		{
			float textureDistance = 0f;
			for (int i = 0; i < hillProfile.Stations.Count - 1; i++)
			{
				Vector3 start = ToGroundPoint(hillProfile.ToCadPoint(hillProfile.Stations[i], lateral));
				Vector3 end = ToGroundPoint(hillProfile.ToCadPoint(hillProfile.Stations[i + 1], lateral));
				start.y = hillProfile.GetStationHeight(i);
				end.y = hillProfile.GetStationHeight(i + 1);
				DrawCurbSegment(start, end, width, Mathf.Max(hillBarrierHeight, MinimumHillBarrierHeight), curbVertices, curbTriangles, planeHeight, curbUVs, textureDistance);
				textureDistance += Vector3.Distance(start, end);
			}
		}
		CreateChildMesh(GeneratedHillCurbsName, $"{GeneratedHillCurbsName} Mesh",
			curbVertices, curbTriangles, hillCurbMaterial != null ? hillCurbMaterial : curbMaterial, true, curbUVs);
	}

	private void AddHillSideWall(Vector3 start, Vector3 end, bool reverseWinding,
		List<Vector3> hillVertices, List<int> hillTriangles)
	{
		Vector3 bottomStart = start;
		Vector3 bottomEnd = end;
		bottomStart.y = planeHeight;
		bottomEnd.y = planeHeight;
		int firstVertex = hillVertices.Count;
		hillVertices.Add(start);
		hillVertices.Add(end);
		hillVertices.Add(bottomStart);
		hillVertices.Add(bottomEnd);

		if (reverseWinding)
		{
			hillTriangles.Add(firstVertex);
			hillTriangles.Add(firstVertex + 2);
			hillTriangles.Add(firstVertex + 1);
			hillTriangles.Add(firstVertex + 1);
			hillTriangles.Add(firstVertex + 2);
			hillTriangles.Add(firstVertex + 3);
		}
		else
		{
			hillTriangles.Add(firstVertex);
			hillTriangles.Add(firstVertex + 1);
			hillTriangles.Add(firstVertex + 2);
			hillTriangles.Add(firstVertex + 1);
			hillTriangles.Add(firstVertex + 3);
			hillTriangles.Add(firstVertex + 2);
		}
	}

    private bool TryGetCadBounds(List<MapData> mapDataList, List<MapData> hillDataList, out Vector2 min, out Vector2 max)
    {
        bool hasPoint = false;
        min = Vector2.zero;
        max = Vector2.zero;

        AddBoundsFromMapData(mapDataList, ref min, ref max, ref hasPoint);
        AddBoundsFromMapData(hillDataList, ref min, ref max, ref hasPoint);
		if (hillProfile.IsValid)
		{
			foreach (float station in hillProfile.Stations)
			{
				Encapsulate(hillProfile.ToCadPoint(station, hillProfile.MinAcross), ref min, ref max, ref hasPoint);
				Encapsulate(hillProfile.ToCadPoint(station, hillProfile.MaxAcross), ref min, ref max, ref hasPoint);
			}
		}
        return hasPoint;
    }

    private void AddBoundsFromMapData(List<MapData> mapDataList, ref Vector2 min, ref Vector2 max, ref bool hasPoint)
    {
        if (mapDataList == null)
        {
            return;
        }

        foreach (var data in mapDataList)
        {
            if (!ShouldDraw(data) && !IsHillData(data))
            {
                continue;
            }

            Encapsulate(GetStartPoint(data), ref min, ref max, ref hasPoint);
            Encapsulate(GetEndPoint(data), ref min, ref max, ref hasPoint);
            EncapsulateBounds(data, ref min, ref max, ref hasPoint);
        }
    }

    private GameObject CreateChildMesh(string childName, string meshName, List<Vector3> meshVertices, List<int> meshTriangles, Material material, bool addCollider, List<Vector2> meshUVs = null)
    {
        if (meshVertices.Count == 0 || meshTriangles.Count == 0)
        {
            return null;
        }

        var child = new GameObject(childName);
        child.transform.SetParent(transform, false);

        var meshFilter = child.AddComponent<MeshFilter>();
        var meshRenderer = child.AddComponent<MeshRenderer>();
        var childMesh = new Mesh
        {
            name = meshName
        };
        if (meshVertices.Count > 65535)
        {
            childMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        childMesh.SetVertices(meshVertices);
		if (meshUVs != null)
		{
			childMesh.SetUVs(0, meshUVs);
		}
        childMesh.SetTriangles(meshTriangles, 0);
        childMesh.RecalculateNormals();
        childMesh.RecalculateBounds();
        meshFilter.sharedMesh = childMesh;
        meshRenderer.sharedMaterial = material != null ? material : GetDefaultMaterial(childName);

        if (addCollider)
        {
            var meshCollider = child.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = childMesh;
        }

        return child;
    }

    private Material GetDefaultMaterial(string materialName)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        var material = new Material(shader)
        {
            name = $"{materialName} Material"
        };
        if (materialName.Contains("Plane"))
        {
            material.color = new Color(0.45f, 0.45f, 0.45f, 1f);
        }
        else if (materialName == GeneratedHillCurbsName)
		{
			material.color = Color.gray;
		}
        else if (materialName.Contains("Hill"))
        {
            material.color = new Color(0.35f, 0.55f, 0.35f, 1f);
        }
        else
        {
            material.color = Color.white;
        }

        return material;
    }

    private void GetPolylinePoints(MapData data, List<Vector2> points)
    {
        points.Clear();
        AddIfValidPoint(points, GetStartPoint(data));
        AddIfValidPoint(points, new Vector2(data.PosX, data.PosY));
        AddIfValidPoint(points, GetEndPoint(data));
    }

    private void AddIfValidPoint(List<Vector2> points, Vector2 point)
    {
        if (point != Vector2.zero)
        {
            points.Add(point);
        }
    }

    private float GetLineWidth(MapData data)
    {
        if (data.GlobalWidth > 0f)
        {
            return Mathf.Max(data.GlobalWidth * cadUnitScale, MinimumLineWidth);
        }

        return Mathf.Max(defaultLineWidth, MinimumLineWidth);
    }

    private void DrawPolyline(MapData data, float width)
    {
        GetPolylinePoints(data, reusablePolylinePoints);

        if (reusablePolylinePoints.Count < 2)
        {
            return;
        }

        DrawPolylineSegments(reusablePolylinePoints, reusablePolylinePoints.Count, width, data.Close.Equals("TRUE", System.StringComparison.OrdinalIgnoreCase));
    }

    private Vector2 GetStartPoint(MapData data)
    {
        Vector2 start = new Vector2(data.StartX, data.StartY);
        return start != Vector2.zero ? start : new Vector2(data.PosX, data.PosY);
    }

    private Vector2 GetEndPoint(MapData data)
    {
        Vector2 end = new Vector2(data.EndX, data.EndY);
        return end != Vector2.zero ? end : new Vector2(data.PosX + data.DeltaX, data.PosY + data.DeltaY);
    }

    private Vector3 ToGroundPoint(Vector2 cadPoint)
    {
        Vector2 scaled = (cadPoint - cadOrigin) * cadUnitScale;
        return new Vector3(scaled.x, 0f, scaled.y);
    }

	private float GetGroundHeight(Vector2 cadPoint)
	{
		return hillProfile.GetHeight(cadPoint);
	}

    private Vector2 GetCadOrigin(List<MapData> mapDataList)
    {
        if (!centerMapOnOrigin)
        {
            return Vector2.zero;
        }

        bool hasPoint = false;
        Vector2 min = Vector2.zero;
        Vector2 max = Vector2.zero;

        foreach (var data in mapDataList)
        {
            if (!ShouldDraw(data))
            {
                continue;
            }

            Encapsulate(GetStartPoint(data), ref min, ref max, ref hasPoint);
            Encapsulate(GetEndPoint(data), ref min, ref max, ref hasPoint);
            EncapsulateBounds(data, ref min, ref max, ref hasPoint);
        }

        return hasPoint ? (min + max) * 0.5f : Vector2.zero;
    }

    private void Encapsulate(Vector2 point, ref Vector2 min, ref Vector2 max, ref bool hasPoint)
    {
        if (point == Vector2.zero)
        {
            return;
        }

        if (!hasPoint)
        {
            min = point;
            max = point;
            hasPoint = true;
            return;
        }

        min = Vector2.Min(min, point);
        max = Vector2.Max(max, point);
    }

    private void EncapsulateBounds(MapData data, ref Vector2 min, ref Vector2 max, ref bool hasPoint)
    {
        Vector2 center = new Vector2(data.CentorPointX, data.CentorPointY);
        if (center == Vector2.zero)
        {
            return;
        }

        if (data.Name == "Circle" && data.Radius > 0f)
        {
            Vector2 radius = Vector2.one * data.Radius;
            Encapsulate(center - radius, ref min, ref max, ref hasPoint);
            Encapsulate(center + radius, ref min, ref max, ref hasPoint);
            return;
        }

        if (data.Name == "Ellipse" && data.MajorRadius > 0f && data.MinorRadius > 0f)
        {
            Vector2 majorAxis = new Vector2(data.MajorVectorX, data.MajorVectorY).normalized * data.MajorRadius;
            Vector2 minorAxis = new Vector2(data.MinorVectorX, data.MinorVectorY).normalized * data.MinorRadius;
            Encapsulate(center + majorAxis + minorAxis, ref min, ref max, ref hasPoint);
            Encapsulate(center + majorAxis - minorAxis, ref min, ref max, ref hasPoint);
            Encapsulate(center - majorAxis + minorAxis, ref min, ref max, ref hasPoint);
            Encapsulate(center - majorAxis - minorAxis, ref min, ref max, ref hasPoint);
            return;
        }

        Encapsulate(center, ref min, ref max, ref hasPoint);
    }

    private float GetEllipseTotalAngle(MapData data)
    {
        if (data.Area <= Mathf.Epsilon || data.MajorRadius <= Mathf.Epsilon || data.MinorRadius <= Mathf.Epsilon)
        {
            return FullAngle;
        }

        float target = Mathf.Clamp(2f * data.Area / (data.MajorRadius * data.MinorRadius), 0f, 2f * Mathf.PI);
        float low = 0f;
        float high = 2f * Mathf.PI;

        for (int i = 0; i < 24; i++)
        {
            float middle = (low + high) * 0.5f;
            float segmentAreaFactor = middle - Mathf.Sin(middle);
            if (segmentAreaFactor < target)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        float angle = (low + high) * 0.5f * Mathf.Rad2Deg;
        return Mathf.Clamp(angle, 1f, FullAngle);
    }

    private bool IsAnnotationLayer(string layerName)
    {
        if (string.IsNullOrWhiteSpace(layerName))
        {
            return false;
        }

        annotationLayerSet ??= BuildLayerSet(annotationLayers);
        return annotationLayerSet.Contains(layerName.Trim());
    }

    private HashSet<string> BuildLayerSet(string layerNames)
    {
        var layerSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(layerNames))
        {
            return layerSet;
        }

        var splitLayerNames = layerNames.Split(',');
        for (int i = 0; i < splitLayerNames.Length; i++)
        {
            var layerName = splitLayerNames[i].Trim();
            if (!string.IsNullOrEmpty(layerName))
            {
                layerSet.Add(layerName);
            }
        }

        return layerSet;
    }

    private void DestroyMesh(Mesh targetMesh)
    {
        if (Application.isPlaying)
        {
            Destroy(targetMesh);
        }
        else
        {
            DestroyImmediate(targetMesh);
        }
    }
}
