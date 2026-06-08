using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MapGenerator : MonoBehaviour
{
    public string mapName;
    [Header("Corner Settings")]
    [SerializeField] private int circleSegments = 32; // 원호를 그릴 때 사용할 세그먼트 수

    [Header("CAD Settings")]
    [SerializeField] private float cadUnitScale = 0.001f;
    [SerializeField] private float defaultLineWidth = 0.2f;
    [SerializeField] private bool centerMapOnOrigin = true;

    private List<Vector3> vertices = new List<Vector3>();
    private List<int> triangles = new List<int>();
    private Mesh mesh;
    private Vector2 cadOrigin;

    public void SetMap(string s)
    {
        mapName = s;
        ClearMap();

        var mapDataList = CsvParaser.GetMapData(mapName);
        cadOrigin = GetCadOrigin(mapDataList);

        foreach (var data in mapDataList)
        {
            if (!ShouldDraw(data))
            {
                continue;
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
                    width);
            }
        }

        ApplyToMesh();
    }

    [Button]
    public void GenerateMap()
    {
        SetMap(mapName);
    }

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
        if (majorRadius <= Mathf.Epsilon || minorRadius <= Mathf.Epsilon)
        {
            return;
        }

        Vector2 majorAxis = majorVector.sqrMagnitude > Mathf.Epsilon ? majorVector.normalized * majorRadius : Vector2.right * majorRadius;
        Vector2 minorAxis = minorVector.sqrMagnitude > Mathf.Epsilon ? minorVector.normalized * minorRadius : Vector2.up * minorRadius;
        Vector2[] points = new Vector2[circleSegments];

        for (int i = 0; i < circleSegments; i++)
        {
            float angle = i * 360f / circleSegments * Mathf.Deg2Rad;
            points[i] = center + majorAxis * Mathf.Cos(angle) + minorAxis * Mathf.Sin(angle);
        }

        DrawPolyline(points, width, true);
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
    }

    public void ClearMap()
    {
        vertices.Clear();
        triangles.Clear();
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

    private bool ShouldDraw(MapData data)
    {
        return data.Name == "Line"
            || data.Name == "PolyLine"
            || data.Name == "Arc"
            || data.Name == "Ellipse";
    }

    private float GetLineWidth(MapData data)
    {
        if (data.GlobalWidth > 0f)
        {
            return Mathf.Max(data.GlobalWidth * cadUnitScale, 0.01f);
        }

        return Mathf.Max(defaultLineWidth, 0.01f);
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
        return new Vector3(scaled.x, 0f, scaled.y);
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
            Encapsulate(new Vector2(data.CentorPointX, data.CentorPointY), ref min, ref max, ref hasPoint);
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
