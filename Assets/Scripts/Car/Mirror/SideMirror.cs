using UnityEngine;

public class SideMirror : MonoBehaviour
{
    [Header("References")]
    public Transform driverEye;       // 기존 직렬화 연결 유지용. 수동 카메라 각도에는 사용하지 않는다.
    public Camera mirrorCam;          // 거울용 카메라
    public Renderer mirrorRenderer;   // 거울 Quad
	public Material displayMaterial;

    [Header("Settings")]
    public Vector2Int resolution = new Vector2Int(512, 256);
    [Range(10f, 90f)] public float fov = 30f;   // 기존 직렬화 연결 유지용. 시야각은 Camera에서 설정한다.
    [Min(1)] public int updateEveryNFrames = 1; // 2~3이면 성능 절약
	[Tooltip("켜면 카메라 위치를 유리면 중심으로 이동합니다. 수동 배치한 위치를 쓰려면 끄세요.")]
	public bool alignCameraToGlassCenter;
	[Tooltip("거울 유리면의 UV 방향에 맞춰 영상의 좌우를 반전합니다.")]
	public bool flipHorizontal = true;

    RenderTexture rt;
	Material runtimeMaterial;
	Material originalMaterial;

    void Start()
    {
        rt = new RenderTexture(resolution.x, resolution.y, 16);
        rt.name = $"{name}_RT";

		mirrorCam.targetTexture = rt;

        // URP는 _BaseMap, Built-in은 _MainTex
		originalMaterial = mirrorRenderer.sharedMaterial;
		runtimeMaterial = new Material(displayMaterial != null ? displayMaterial : originalMaterial);
		mirrorRenderer.sharedMaterial = runtimeMaterial;
		Material mat = runtimeMaterial;
		ConfigureMirrorTextureCoordinates(mat);
        string texProp = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        mat.SetTexture(texProp, rt);

		// 모델마다 유리면 UV 방향이 달라 거울별로 반전을 선택한다.
		float horizontalScale = flipHorizontal ? -1f : 1f;
		float horizontalOffset = flipHorizontal ? 1f : 0f;
		mat.SetTextureScale(texProp, new Vector2(horizontalScale, 1f));
		mat.SetTextureOffset(texProp, new Vector2(horizontalOffset, 0f));
    }

    void LateUpdate()
    {
		if (alignCameraToGlassCenter)
		{
			// 메쉬 원점 대신 유리면 중심을 따라가되, 설정한 로컬 각도는 유지한다.
			mirrorCam.transform.position = mirrorRenderer.transform.TransformPoint(mirrorRenderer.localBounds.center);
		}

        mirrorCam.enabled = Time.frameCount % updateEveryNFrames == 0;
    }

	void ConfigureMirrorTextureCoordinates(Material material)
	{
		if (!material.HasProperty("_UsePlanarUV") ||
			!mirrorRenderer.TryGetComponent(out MeshFilter meshFilter) ||
			meshFilter.sharedMesh == null)
		{
			return;
		}

		Mesh mesh = meshFilter.sharedMesh;
		if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
		{
			material.SetFloat("_UsePlanarUV", 0f);
			return;
		}

		// UV가 없는 유리면은 가장 넓은 두 로컬 축에 투영한다. 정점 읽기가 필요 없어 Read/Write를 켜지 않는다.
		Bounds bounds = mesh.bounds;
		Vector3 size = bounds.size;
		int horizontalAxis = 0;
		for (int axis = 1; axis < 3; axis++)
		{
			if (size[axis] > size[horizontalAxis])
			{
				horizontalAxis = axis;
			}
		}

		int verticalAxis = horizontalAxis == 0 ? 1 : 0;
		for (int axis = 0; axis < 3; axis++)
		{
			if (axis != horizontalAxis && size[axis] > size[verticalAxis])
			{
				verticalAxis = axis;
			}
		}

		Vector3 horizontal = Vector3.zero;
		Vector3 vertical = Vector3.zero;
		horizontal[horizontalAxis] = 1f;
		vertical[verticalAxis] = 1f;
		material.SetFloat("_UsePlanarUV", 1f);
		material.SetVector("_MirrorUvMin", bounds.min);
		material.SetVector("_MirrorUvSize", size);
		material.SetVector("_MirrorUvHorizontal", horizontal);
		material.SetVector("_MirrorUvVertical", vertical);
	}

    void OnDestroy()
    {
		if (mirrorRenderer != null && mirrorRenderer.sharedMaterial == runtimeMaterial)
		{
			mirrorRenderer.sharedMaterial = originalMaterial;
		}

		if (mirrorCam != null && mirrorCam.targetTexture == rt)
		{
			mirrorCam.targetTexture = null;
		}

		if (rt != null)
		{
			rt.Release();
			Destroy(rt);
		}

		if (runtimeMaterial != null)
		{
			Destroy(runtimeMaterial);
		}
    }
}
