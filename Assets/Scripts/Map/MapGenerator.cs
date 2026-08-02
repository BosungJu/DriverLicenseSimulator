using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MapGenerator : MonoBehaviour
{
    private const float MinimumLineWidth = 0.01f;
    private const float FullAngle = 360f;

    public string mapName;
    public Mesh CurrentMesh => mesh;

    [Header("Corner Settings")]
    [SerializeField] private int circleSegments = 64; // 원호를 그릴 때 사용할 세그먼트 수

    [Header("CAD Settings")]
    [SerializeField] private float cadUnitScale = 0.001f;
    [SerializeField] private float defaultLineWidth = 0.2f;
    [SerializeField] private bool centerMapOnOrigin = true;
    [SerializeField] private bool preferOriginCsv = true;
    [SerializeField] private bool skipAnnotationLayers = true;
    [SerializeField] private string annotationLayers = "DIM,Defpoints";

    [Header("Plane Settings")]
    [SerializeField] private bool generateBasePlane = true;
    [SerializeField] private float planePadding = 2f;
    [SerializeField] private float planeHeight = -0.02f;
    [SerializeField] private float lineHeightOffset = 0.03f;
    [SerializeField] private Material planeMaterial;
    [SerializeField] private Material lineMaterial;

    [Header("Hill Settings")]
    [SerializeField] private bool generateHillSurface = true;
    [SerializeField] private bool importHillFromLegacyCsv = true;
    [SerializeField] private string legacyHillMapName = "MapData/옥천학원수정";
    [SerializeField] private string hillLayerName = "Hill";
    [SerializeField] private float hillHeight = 1.5f;
    [SerializeField] private Material hillMaterial;

    [Header("Open Shape Repair")]
    [SerializeField] private bool fillOpenEndsWithNearestTriangle = true;
    [SerializeField] private float openEndpointTolerance = 1f;
    [SerializeField] private float nearestTriangleMaxDistance = 5000f;

    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private HashSet<string> annotationLayerSet;
    private Mesh mesh;
    private Vector2 cadOrigin;
    private bool hasHillProfile;
    private float hillProfileMinY;
    private float hillProfileMaxY;
    private List<float> hillProfileXValues = new List<float>();

    private struct CadGraphEndpoint
    {
        public readonly Vector2 Point;
        public readonly Vector2 ConnectedPoint;

        public CadGraphEndpoint(Vector2 point, Vector2 connectedPoint)
        {
            Point = point;
            ConnectedPoint = connectedPoint;
        }
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
            DrawMapEntity(data);
        }

        if (fillOpenEndsWithNearestTriangle)
        {
            DrawNearestTriangleOpenEndRepairs(mapDataList);
        }

        ApplyToMesh();
    }

    [Button]
    public void GenerateMap()
    {
        SetMap(mapName);
    }

#if UNITY_EDITOR
    [Button]
    public void SaveGeneratedMeshAsset()
    {
        if (mesh == null || vertices.Count == 0)
        {
            GenerateMap();
        }

        if (mesh == null || vertices.Count == 0)
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

        Mesh meshAsset = Instantiate(mesh);
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

        for (int i = 0; i < points.Length - 1; i++)
        {
            DrawLine(points[i], points[i + 1], width);
        }

        if (isClosed)
        {
            DrawLine(points[points.Length - 1], points[0], width);
        }
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

    public void ApplyToMesh()
    {
        if (mesh != null)
        {
            DestroyMesh(mesh);
        }

        mesh = new Mesh { name = "Generated Map Mesh" };
        if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;

        if (!TryGetComponent<MeshCollider>(out var meshCollider))
        {
            meshCollider = gameObject.AddComponent<MeshCollider>();
        }

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;

        if (lineMaterial != null && TryGetComponent<MeshRenderer>(out var meshRenderer))
        {
            meshRenderer.sharedMaterial = lineMaterial;
        }
    }

    public void ClearMap()
    {
        vertices.Clear();
        triangles.Clear();
        ClearGeneratedChild("Generated Base Plane");
        ClearGeneratedChild("Generated Hill Surface");

        if (mesh != null)
        {
            DestroyMesh(mesh);
            mesh = null;
        }

        if (TryGetComponent<MeshFilter>(out var meshFilter))
        {
            meshFilter.sharedMesh = null;
        }

        if (TryGetComponent<MeshCollider>(out var meshCollider))
        {
            meshCollider.sharedMesh = null;
        }
    }

    private void ClearGeneratedChild(string childName)
    {
        var child = transform.Find(childName);
        if (child == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(child.gameObject);
        }
        else
        {
            DestroyImmediate(child.gameObject);
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
        if (!preferOriginCsv || string.IsNullOrWhiteSpace(requestedMapName))
        {
            return requestedMapName;
        }

        string extension = System.IO.Path.GetExtension(requestedMapName);
        string pathWithoutExtension = string.IsNullOrEmpty(extension)
            ? requestedMapName
            : requestedMapName.Substring(0, requestedMapName.Length - extension.Length);

        if (pathWithoutExtension.EndsWith("_origin", System.StringComparison.OrdinalIgnoreCase))
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

        CreateChildMesh("Generated Hill Surface", "Generated Hill Surface Mesh", hillVertices, hillTriangles, hillMaterial, true);
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

    private void CreateChildMesh(string childName, string meshName, List<Vector3> meshVertices, List<int> meshTriangles, Material material, bool addCollider)
    {
        if (meshVertices.Count == 0 || meshTriangles.Count == 0)
        {
            return;
        }

        var child = new GameObject(childName);
        child.transform.SetParent(transform, false);

        var meshFilter = child.AddComponent<MeshFilter>();
        var meshRenderer = child.AddComponent<MeshRenderer>();
        var childMesh = new Mesh { name = meshName };
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
    }

    private Material GetDefaultMaterial(string materialName)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        var material = new Material(shader) { name = $"{materialName} Material" };
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

    private void DrawNearestTriangleOpenEndRepairs(List<MapData> mapDataList)
    {
        var degrees = new Dictionary<string, int>();
        var candidates = new List<Vector2>();
        var endpoints = new List<CadGraphEndpoint>();
        var existingEdges = new HashSet<string>();

        foreach (var data in mapDataList)
        {
            if (ShouldDraw(data))
            {
                AddGraphData(data, degrees, candidates, endpoints, existingEdges);
                continue;
            }

            if (data.Name == "Point" && IsConnectionCandidateLayer(data.Layer))
            {
                AddCandidate(new Vector2(data.PosX, data.PosY), candidates);
            }
        }

        foreach (var endpoint in endpoints)
        {
            if (!degrees.TryGetValue(GetPointKey(endpoint.Point), out int degree) || degree != 1)
            {
                continue;
            }

            if (!TryFindNearestTrianglePoint(endpoint, candidates, existingEdges, out var target))
            {
                continue;
            }

            string edgeKey = GetEdgeKey(endpoint.Point, target);
            if (!existingEdges.Add(edgeKey))
            {
                continue;
            }

            DrawTriangle(endpoint.Point, endpoint.ConnectedPoint, target);
        }
    }

    private void AddGraphData(
        MapData data,
        Dictionary<string, int> degrees,
        List<Vector2> candidates,
        List<CadGraphEndpoint> endpoints,
        HashSet<string> existingEdges)
    {
        float width = GetLineWidth(data);

        if (data.Name == "Line")
        {
            AddGraphEdge(GetStartPoint(data), GetEndPoint(data), width, degrees, candidates, endpoints, existingEdges);
        }
        else if (data.Name == "PolyLine")
        {
            var points = GetPolylinePoints(data);
            for (int i = 0; i < points.Count - 1; i++)
            {
                AddGraphEdge(points[i], points[i + 1], width, degrees, candidates, endpoints, existingEdges);
            }

            if (points.Count > 2 && data.Close.Equals("TRUE", System.StringComparison.OrdinalIgnoreCase))
            {
                AddGraphEdge(points[points.Count - 1], points[0], width, degrees, candidates, endpoints, existingEdges);
            }
        }
        else if (data.Name == "Arc")
        {
            AddGraphEdge(
                GetArcPoint(new Vector2(data.CentorPointX, data.CentorPointY), data.Radius, data.StartDegree),
                GetArcPoint(new Vector2(data.CentorPointX, data.CentorPointY), data.Radius, data.StartDegree + data.TotalAngle),
                width,
                degrees,
                candidates,
                endpoints,
                existingEdges);
        }
        else if (data.Name == "Ellipse")
        {
            float totalAngle = GetEllipseTotalAngle(data);
            if (Mathf.Abs(Mathf.Abs(totalAngle) - FullAngle) > 0.1f)
            {
                AddGraphEdge(
                    GetEllipsePoint(data, data.StartDegree),
                    GetEllipsePoint(data, data.StartDegree + totalAngle),
                    width,
                    degrees,
                    candidates,
                    endpoints,
                    existingEdges);
            }
        }
    }

    private void AddGraphEdge(
        Vector2 start,
        Vector2 end,
        float width,
        Dictionary<string, int> degrees,
        List<Vector2> candidates,
        List<CadGraphEndpoint> endpoints,
        HashSet<string> existingEdges)
    {
        if ((end - start).sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        IncrementDegree(start, degrees);
        IncrementDegree(end, degrees);
        AddCandidate(start, candidates);
        AddCandidate(end, candidates);
        endpoints.Add(new CadGraphEndpoint(start, end));
        endpoints.Add(new CadGraphEndpoint(end, start));
        existingEdges.Add(GetEdgeKey(start, end));
    }

    private bool TryFindNearestTrianglePoint(CadGraphEndpoint endpoint, List<Vector2> candidates, HashSet<string> existingEdges, out Vector2 target)
    {
        target = Vector2.zero;
        float tolerance = GetConnectionTolerance();
        float bestDistance = float.PositiveInfinity;

        foreach (var candidate in candidates)
        {
            if ((candidate - endpoint.Point).sqrMagnitude <= tolerance * tolerance)
            {
                continue;
            }

            if ((candidate - endpoint.ConnectedPoint).sqrMagnitude <= tolerance * tolerance)
            {
                continue;
            }

            if (existingEdges.Contains(GetEdgeKey(endpoint.Point, candidate)))
            {
                continue;
            }

            float distance = Vector2.Distance(endpoint.Point, candidate);
            if (nearestTriangleMaxDistance > 0f && distance > nearestTriangleMaxDistance)
            {
                continue;
            }

            if (IsDegenerateTriangle(endpoint.Point, endpoint.ConnectedPoint, candidate))
            {
                continue;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                target = candidate;
            }
        }

        return bestDistance < float.PositiveInfinity;
    }

    private void DrawTriangle(Vector2 a, Vector2 b, Vector2 c)
    {
        if (IsDegenerateTriangle(a, b, c))
        {
            return;
        }

        Vector3 worldA = ToWorldPoint(a);
        Vector3 worldB = ToWorldPoint(b);
        Vector3 worldC = ToWorldPoint(c);
        int baseIdx = vertices.Count;

        vertices.Add(worldA);
        vertices.Add(worldB);
        vertices.Add(worldC);

        Vector3 normal = Vector3.Cross(worldB - worldA, worldC - worldA);
        if (normal.y >= 0f)
        {
            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 1);
            triangles.Add(baseIdx + 2);
        }
        else
        {
            triangles.Add(baseIdx);
            triangles.Add(baseIdx + 2);
            triangles.Add(baseIdx + 1);
        }
    }

    private bool IsDegenerateTriangle(Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
        return area <= GetConnectionTolerance();
    }

    private List<Vector2> GetPolylinePoints(MapData data)
    {
        var points = new List<Vector2>
        {
            GetStartPoint(data),
            new Vector2(data.PosX, data.PosY),
            GetEndPoint(data)
        };

        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (points[i] == Vector2.zero)
            {
                points.RemoveAt(i);
            }
        }

        return points;
    }

    private Vector2 GetArcPoint(Vector2 center, float radius, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return new Vector2(
            center.x + Mathf.Cos(radians) * radius,
            center.y + Mathf.Sin(radians) * radius);
    }

    private Vector2 GetEllipsePoint(MapData data, float angle)
    {
        Vector2 center = new Vector2(data.CentorPointX, data.CentorPointY);
        Vector2 majorVector = new Vector2(data.MajorVectorX, data.MajorVectorY);
        Vector2 minorVector = new Vector2(data.MinorVectorX, data.MinorVectorY);
        Vector2 majorAxis = majorVector.sqrMagnitude > Mathf.Epsilon ? majorVector.normalized * data.MajorRadius : Vector2.right * data.MajorRadius;
        Vector2 minorAxis = minorVector.sqrMagnitude > Mathf.Epsilon ? minorVector.normalized * data.MinorRadius : Vector2.up * data.MinorRadius;
        float radians = angle * Mathf.Deg2Rad;
        return center + majorAxis * Mathf.Cos(radians) + minorAxis * Mathf.Sin(radians);
    }

    private void IncrementDegree(Vector2 point, Dictionary<string, int> degrees)
    {
        string key = GetPointKey(point);
        degrees.TryGetValue(key, out int count);
        degrees[key] = count + 1;
    }

    private void AddCandidate(Vector2 point, List<Vector2> candidates)
    {
        if (point != Vector2.zero)
        {
            candidates.Add(point);
        }
    }

    private string GetPointKey(Vector2 point)
    {
        float tolerance = GetConnectionTolerance();
        int x = Mathf.RoundToInt(point.x / tolerance);
        int y = Mathf.RoundToInt(point.y / tolerance);
        return $"{x}:{y}";
    }

    private string GetEdgeKey(Vector2 a, Vector2 b)
    {
        string aKey = GetPointKey(a);
        string bKey = GetPointKey(b);
        return string.CompareOrdinal(aKey, bKey) <= 0 ? $"{aKey}|{bKey}" : $"{bKey}|{aKey}";
    }

    private float GetConnectionTolerance()
    {
        return Mathf.Max(openEndpointTolerance, 0.001f);
    }

    private bool IsConnectionCandidateLayer(string layerName)
    {
        return !skipAnnotationLayers || !IsAnnotationLayer(layerName);
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
        List<Vector2> points = new List<Vector2>
        {
            GetStartPoint(data),
            new Vector2(data.PosX, data.PosY),
            GetEndPoint(data)
        };

        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (points[i] == Vector2.zero)
            {
                points.RemoveAt(i);
            }
        }

        if (points.Count < 2)
        {
            return;
        }

        DrawPolyline(points.ToArray(), width, data.Close.Equals("TRUE", System.StringComparison.OrdinalIgnoreCase));
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
