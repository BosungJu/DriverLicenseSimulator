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
    private const float FullAngle = 360f;
    private const string LineLayerName = "Line";
    private const string TestLayerName = "T_Test";
    private const string RoutePointLayerName = "Point";
    private const string GeneratedLineMeshName = "Generated Line Layer";
    private const string GeneratedTestMeshName = "Generated T_Test Layer";
    private const string GeneratedRoutePointsName = "Generated Route Points";
    private const string GeneratedRepairsName = "Generated Open End Repairs";
    private const string GeneratedAdditionalLinesName = "Generated Additional Lines";

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
    [SerializeField] private Material hillMaterial;

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
    private bool hasHillProfile;
    private float hillProfileMinY;
    private float hillProfileMaxY;
    private List<float> hillProfileXValues = new List<float>();

    private sealed class LayerMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();
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
        hasHillProfile = false;
        hillProfileXValues.Clear();
        if (mapDataList == null || mapDataList.Count == 0)
        {
            MapGenerated?.Invoke();
            return;
        }

        cadOrigin = GetCadOrigin(mapDataList);
        var hillDataList = GetHillData(mapDataList);
        BuildHillProfile(hillDataList);

        if (generateBasePlane)
        {
            GenerateBasePlane(mapDataList, hillDataList);
        }

        if (generateHillSurface)
        {
            GenerateHillSurface(hillDataList);
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

        if (HasSensorMesh(GeneratedLineMeshName) && HasSensorMesh(GeneratedTestMeshName))
        {
            return;
        }

        string resolvedMapName = ResolveMapName(mapName);
        List<MapData> mapDataList = CsvParaser.GetMapData(resolvedMapName);
        if (mapDataList.Count == 0)
        {
            return;
        }

        mapName = resolvedMapName;
        SetMapData(mapDataList);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private bool HasSensorMesh(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null
            && child.TryGetComponent<MeshFilter>(out var meshFilter)
            && meshFilter.sharedMesh != null
            && meshFilter.sharedMesh.vertexCount > 0
            && child.TryGetComponent<MeshRenderer>(out var meshRenderer)
            && meshRenderer.enabled
            && meshRenderer.sharedMaterial != null
            && child.TryGetComponent<MeshCollider>(out var meshCollider)
            && meshCollider.sharedMesh == meshFilter.sharedMesh;
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

        string fileName = string.IsNullOrWhiteSpace(mapName)
            ? "GeneratedMapMesh"
            : System.IO.Path.GetFileNameWithoutExtension(mapName);
        string path = UnityEditor.EditorUtility.SaveFilePanelInProject(
            "Save Generated Map Mesh",
            $"{fileName}_Mesh",
            "asset",
            "Choose where to save the generated map mesh asset.");

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var combineInstances = new List<CombineInstance>();
        if (mesh.vertexCount > 0)
        {
            combineInstances.Add(new CombineInstance { mesh = mesh });
        }

        foreach (Transform child in transform)
        {
            if (child.TryGetComponent<MapLayerCollisionRelay>(out _)
                && child.TryGetComponent<MeshFilter>(out var layerFilter)
                && layerFilter.sharedMesh != null)
            {
                combineInstances.Add(new CombineInstance { mesh = layerFilter.sharedMesh });
            }
        }

        Mesh meshAsset = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        meshAsset.CombineMeshes(combineInstances.ToArray(), true, false);
        meshAsset.name = $"{fileName}_Mesh";

        path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(path);
        UnityEditor.AssetDatabase.CreateAsset(meshAsset, path);
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
        Debug.Log($"MapGenerator: saved mesh asset to {path}");
    }
#endif

    public void DrawLine(Vector2 start, Vector2 end, float width)
    {
        if ((end - start).sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        Vector3 s = ToWorldPoint(start);
        Vector3 e = ToWorldPoint(end);

        Vector3 forward = (e - s).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, forward) * (width * 0.5f);

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
            CreateLayerMesh(childName, $"{childName} Mesh", layer.Value.Vertices, layer.Value.Triangles, layer.Key);
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
        ClearGeneratedChild("Generated Base Plane");
        ClearGeneratedChild("Generated Hill Surface");
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
        if (layerName == null)
        {
            vertices = otherLayerVertices;
            triangles = otherLayerTriangles;
            return;
        }

        if (!layerMeshes.TryGetValue(layerName, out LayerMeshData layerData))
        {
            layerData = new LayerMeshData();
            layerMeshes.Add(layerName, layerData);
        }

        vertices = layerData.Vertices;
        triangles = layerData.Triangles;
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
        string layerName)
    {
        GameObject child = CreateChildMesh(childName, meshName, meshVertices, meshTriangles, lineMaterial, true);
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
        if (targetMaterial == planeMaterial || targetMaterial == lineMaterial || targetMaterial == hillMaterial)
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
        CreateChildMesh("Generated Base Plane", "Generated Base Plane Mesh", planeVertices, planeTriangles, planeMaterial, true);
    }

    private void GenerateHillSurface(List<MapData> hillDataList)
    {
        if (!hasHillProfile)
        {
            return;
        }

        var hillVertices = new List<Vector3>();
        var hillTriangles = new List<int>();

        for (int i = 0; i < hillProfileXValues.Count; i++)
        {
            float height = GetHillProfileHeight(i, hillProfileXValues.Count);
            Vector3 lower = ToGroundPoint(new Vector2(hillProfileXValues[i], hillProfileMinY));
            Vector3 upper = ToGroundPoint(new Vector2(hillProfileXValues[i], hillProfileMaxY));
            lower.y = height;
            upper.y = height;
            hillVertices.Add(lower);
            hillVertices.Add(upper);
        }

        for (int i = 0; i < hillProfileXValues.Count - 1; i++)
        {
            int baseIdx = i * 2;
            hillTriangles.Add(baseIdx);
            hillTriangles.Add(baseIdx + 1);
            hillTriangles.Add(baseIdx + 2);
            hillTriangles.Add(baseIdx + 2);
            hillTriangles.Add(baseIdx + 1);
            hillTriangles.Add(baseIdx + 3);
        }

        Material surfaceMaterial = hillMaterial != null ? hillMaterial : planeMaterial;
        CreateChildMesh("Generated Hill Surface", "Generated Hill Surface Mesh", hillVertices, hillTriangles, surfaceMaterial, true);
    }

    private void BuildHillProfile(List<MapData> hillDataList)
    {
        hasHillProfile = false;
        hillProfileXValues.Clear();

        if (hillDataList == null || hillDataList.Count < 2)
        {
            return;
        }

        bool hasBounds = false;

        foreach (var data in hillDataList)
        {
            Vector2 start = GetStartPoint(data);
            Vector2 end = GetEndPoint(data);
            AddUniqueSortedValue(hillProfileXValues, start.x);
            AddUniqueSortedValue(hillProfileXValues, end.x);

            if (!hasBounds)
            {
                hillProfileMinY = Mathf.Min(start.y, end.y);
                hillProfileMaxY = Mathf.Max(start.y, end.y);
                hasBounds = true;
            }
            else
            {
                hillProfileMinY = Mathf.Min(hillProfileMinY, start.y, end.y);
                hillProfileMaxY = Mathf.Max(hillProfileMaxY, start.y, end.y);
            }
        }

        hillProfileXValues.Sort();
        hasHillProfile = hasBounds && hillProfileXValues.Count >= 2;
    }

    private float GetHillProfileHeight(int index, int count)
    {
        if (count <= 1)
        {
            return 0f;
        }

        if (count == 2)
        {
            return index == 0 ? 0f : hillHeight;
        }

        if (count == 3)
        {
            return index == 1 ? hillHeight : 0f;
        }

        return index == 0 || index == count - 1 ? 0f : hillHeight;
    }

    private bool TryGetCadBounds(List<MapData> mapDataList, List<MapData> hillDataList, out Vector2 min, out Vector2 max)
    {
        bool hasPoint = false;
        min = Vector2.zero;
        max = Vector2.zero;

        AddBoundsFromMapData(mapDataList, ref min, ref max, ref hasPoint);
        AddBoundsFromMapData(hillDataList, ref min, ref max, ref hasPoint);
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

    private void AddUniqueSortedValue(List<float> values, float value)
    {
        const float tolerance = 0.001f;
        for (int i = 0; i < values.Count; i++)
        {
            if (Mathf.Abs(values[i] - value) <= tolerance)
            {
                return;
            }
        }

        values.Add(value);
    }

    private GameObject CreateChildMesh(string childName, string meshName, List<Vector3> meshVertices, List<int> meshTriangles, Material material, bool addCollider)
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

    private Vector3 ToWorldPoint(Vector2 cadPoint)
    {
        Vector2 scaled = (cadPoint - cadOrigin) * cadUnitScale;
        return new Vector3(scaled.x, GetGroundHeight(cadPoint) + lineHeightOffset, scaled.y);
    }

    private Vector3 ToGroundPoint(Vector2 cadPoint)
    {
        Vector2 scaled = (cadPoint - cadOrigin) * cadUnitScale;
        return new Vector3(scaled.x, 0f, scaled.y);
    }

    private float GetGroundHeight(Vector2 cadPoint)
    {
        if (!hasHillProfile || cadPoint.y < hillProfileMinY || cadPoint.y > hillProfileMaxY)
        {
            return 0f;
        }

        if (cadPoint.x < hillProfileXValues[0] || cadPoint.x > hillProfileXValues[hillProfileXValues.Count - 1])
        {
            return 0f;
        }

        for (int i = 0; i < hillProfileXValues.Count - 1; i++)
        {
            float startX = hillProfileXValues[i];
            float endX = hillProfileXValues[i + 1];
            if (cadPoint.x < startX || cadPoint.x > endX)
            {
                continue;
            }

            float startHeight = GetHillProfileHeight(i, hillProfileXValues.Count);
            float endHeight = GetHillProfileHeight(i + 1, hillProfileXValues.Count);
            float t = Mathf.InverseLerp(startX, endX, cadPoint.x);
            return Mathf.Lerp(startHeight, endHeight, t);
        }

        return 0f;
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
