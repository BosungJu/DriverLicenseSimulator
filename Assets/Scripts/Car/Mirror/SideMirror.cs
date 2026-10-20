using UnityEngine;

public class SideMirror : MonoBehaviour
{
    [Header("References")]
    public Transform driverEye;       // 운전자 카메라
    public Camera mirrorCam;          // 거울용 카메라
    public Renderer mirrorRenderer;   // 거울 Quad

    [Header("Settings")]
    public Vector2Int resolution = new Vector2Int(512, 256);
    [Range(10f, 90f)] public float fov = 30f;   // 키우면 볼록거울 느낌
    [Min(1)] public int updateEveryNFrames = 1; // 2~3이면 성능 절약

    RenderTexture rt;

    void Start()
    {
        rt = new RenderTexture(resolution.x, resolution.y, 16);
        rt.name = $"{name}_RT";

        mirrorCam.targetTexture = rt;
        mirrorCam.fieldOfView = fov;
        mirrorCam.aspect = (float)resolution.x / resolution.y;
        mirrorCam.nearClipPlane = 0.05f;

        // URP는 _BaseMap, Built-in은 _MainTex
        Material mat = mirrorRenderer.material;
        string texProp = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
        mat.SetTexture(texProp, rt);

        // 거울이니까 좌우 반전
        mat.SetTextureScale(texProp, new Vector2(-1f, 1f));
        mat.SetTextureOffset(texProp, new Vector2(1f, 0f));
    }

    void LateUpdate()
    {
        // 거울 면의 법선 (기본 Quad는 보이는 면이 -Z 방향)
        Vector3 normal = -mirrorRenderer.transform.forward;

        // 운전자 눈 → 거울 방향을 거울 면에 반사
        Vector3 incoming = (mirrorRenderer.transform.position - driverEye.position).normalized;
        Vector3 reflected = Vector3.Reflect(incoming, normal);

        mirrorCam.transform.SetPositionAndRotation(
            mirrorRenderer.transform.position,
            Quaternion.LookRotation(reflected, transform.up));

        mirrorCam.enabled = Time.frameCount % updateEveryNFrames == 0;
    }

    void OnDestroy()
    {
        if (rt != null) rt.Release();
    }
}