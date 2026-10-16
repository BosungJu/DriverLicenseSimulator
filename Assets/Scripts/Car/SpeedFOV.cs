using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine; // Vcam 제어에 사용

public class SpeedFOV : MonoBehaviour
{
    public Rigidbody carRb; // Car의 Rigidbody
    public CinemachineVirtualCamera frontVcam; // DriverEye의 Vcam (정면)
    public Camera leftCam; // LeftCamera
    public Camera rightCam; // RightCamera

    [Header("FOV 설정")]
    public float baseFOV = 65f; // 기본값
    public float maxFOV = 78f; // 최댓값
    public float maxSpeedKmh = 110f; // 이 속도에서 maxFOV 적용
    public float changeSpeed = 3f; // FOV 변화의 부드러운 정도

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        float speedKmh = carRb.linearVelocity.magnitude * 3.6f;

        // 속도 비율(0~1)로 목표 FOV 계산
        float t = Mathf.Clamp01(speedKmh / maxSpeedKmh);
        float targetFOV = Mathf.Lerp(baseFOV, maxFOV, t);
        float lerp = changeSpeed * Time.deltaTime;

        // 정면 카메라는 Vcam 렌즈의 FOV를 변경
        frontVcam.m_Lens.FieldOfView = Mathf.Lerp(frontVcam.m_Lens.FieldOfView, targetFOV, lerp);

        // 좌우 일반 카메라는 FOV를 직접 변경
        leftCam.fieldOfView = Mathf.Lerp(leftCam.fieldOfView, targetFOV, lerp);

        rightCam.fieldOfView = Mathf.Lerp(rightCam.fieldOfView, targetFOV, lerp);
    }
}
