using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WiperShape : MonoBehaviour
{
    [Header("기존 Cube 대비 크기")]
    [Range(0.1f, 1f)]
    public float baseWidth = 0.7f;

    [Range(0.01f, 1f)]
    public float tipWidth = 0.2f;

    [Range(0.1f, 1f)]
    public float thickness = 0.4f;

    private Mesh generatedMesh;
    private bool needsRebuild;

    void OnEnable()
    {
        needsRebuild = true;
    }

    void OnValidate()
    {
        needsRebuild = true;
    }

    void Update()
    {
        if (!needsRebuild)
            return;

        needsRebuild = false;
        BuildMesh();
    }

    void BuildMesh()
    {
        if (generatedMesh == null)
        {
            generatedMesh = new Mesh();
            generatedMesh.name = "TaperedWiper";
            generatedMesh.hideFlags = HideFlags.DontSave;
        }

        float baseHalf = baseWidth * 0.5f;
        float tipHalf = tipWidth * 0.5f;
        float depthHalf = thickness * 0.5f;

        // 길이 방향은 로컬 X축이며, 중심과 전체 길이는 기존 Cube와 동일하다.
        Vector3 a = new Vector3(-0.5f, -baseHalf, -depthHalf);
        Vector3 b = new Vector3(-0.5f,  baseHalf, -depthHalf);
        Vector3 c = new Vector3( 0.5f,  tipHalf,  -depthHalf);
        Vector3 d = new Vector3( 0.5f, -tipHalf,  -depthHalf);

        Vector3 e = new Vector3(-0.5f, -baseHalf, depthHalf);
        Vector3 f = new Vector3(-0.5f,  baseHalf, depthHalf);
        Vector3 g = new Vector3( 0.5f,  tipHalf,  depthHalf);
        Vector3 h = new Vector3( 0.5f, -tipHalf,  depthHalf);

        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        AddFace(vertices, triangles, a, b, c, d);
        AddFace(vertices, triangles, e, h, g, f);
        AddFace(vertices, triangles, a, e, f, b);
        AddFace(vertices, triangles, d, c, g, h);
        AddFace(vertices, triangles, b, f, g, c);
        AddFace(vertices, triangles, a, d, h, e);

        generatedMesh.Clear();
        generatedMesh.SetVertices(vertices);
        generatedMesh.SetTriangles(triangles, 0);
        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = generatedMesh;
    }

    static void AddFace(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);

        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    void OnDestroy()
    {
        if (generatedMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(generatedMesh);
        else
            DestroyImmediate(generatedMesh);
    }
}