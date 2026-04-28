using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    public string mapName;
    [Header("Corner Settings")]
    public int circleSegments = 32; // 원호를 그릴 때 사용할 세그먼트 수

    private List<Vector3> vertices = new List<Vector3>();
    private List<Vector2> uvs = new List<Vector2>();
    private List<int> triangles = new List<int>();
    private Mesh mesh;

    public void SetMap(string s)
    {
        mapName = s;
        var mapDataList = CsvParaser.GetCadData(mapName);
        foreach (var data in mapDataList)
        {
            if (data.Layer == "DIM")
            {
                if (data.Name == "Line")
                {
                    DrawLine(new Vector2(data.PosX, data.PosY), new Vector2(data.EndX, data.EndY), 0.5f);
                }
                else if (data.Name == "PolyLine")
                {
                    DrawPolyline(new Vector2[] {
                        new Vector2(data.StartX, data.StartY),
                        new Vector2(data.PosX, data.PosY),
                        new Vector2(data.EndX, data.EndY)
                    }, 0.5f, false);
                }
                else if (data.Name == "Ellipse")
                {
                    DrawCircle(new Vector2(data.CentorPointX, data.CentorPointY), data.MajorRadius, 0.5f);
                }
            }
            else if (data.Layer == "Hill")
            {
                // TODO 선형 보간을 이용해 곡선 형태로 그리기

            }
        }

        ApplyToMesh();
    }

    public void GenerateMap()
    {

    }

    private void AddVertex(Vector3 vertex)
    {
        vertices.Add(vertex);

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
    }

    public void DrawLine(Vector2 start, Vector2 end, float width)
    {
        Vector3 s = new Vector3(start.x, 0, start.y);
        Vector3 e = new Vector3(end.x, 0, end.y);

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
            points[i] = new Vector2(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius
            );
        }
        DrawPolyline(points, width, true);
    }

    public void DrawArc(Vector2 center, float radius, float startAngle, float endAngle, float width)
    {
        float angleRange = endAngle - startAngle;
        float angleStep = angleRange / circleSegments;
        Vector2[] points = new Vector2[circleSegments + 1];

        for (int i = 0; i <= circleSegments; i++)
        {
            float angle = (startAngle + i * angleStep) * Mathf.Deg2Rad;
            points[i] = new Vector2(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius
            );
        }
        DrawPolyline(points, width, false);
    }

    public void DrawHill(Vector2[] points, float width)
    {
        // TODO 선형 보간을 이용해 곡선 형태로 그리기
        DrawPolyline(points, width, false);
    }

    public void ApplyToMesh()
    {
        if (mesh != null) ClearMap();

        mesh = new GameObject("MapMesh").AddComponent<MeshFilter>().mesh;
        if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().mesh = mesh;
        mesh.GameObject().AddComponent<MeshCollider>();
    }

    public void ClearMap()
    {
        vertices.Clear();
        triangles.Clear();
        if (mesh != null) Destroy(mesh);
    }
}
