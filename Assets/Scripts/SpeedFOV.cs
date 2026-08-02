using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine; //vcam 제어에 사용

public class SpeedFOV : MonoBehaviour
{
    public Rigidbody carRb; //Car의 Rigidbody
    public CinemachineVirtualCamera frontVcam; //DriverEye의 Vcam (정면)
    public Camera leftCam; //LeftCamera
    public Camera rightCam; //RightCamera

    [Header("FOV 설정")]
    public float baseFOV = 65f; //정지 시
    public float maxFOV = 78f; //최고속 시
    public float maxSpeedKmh = 110f; //이 속도에서 maxFOV 도달
    public float changeSpeed = 3f; //FOV 변하는 부드러움

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float speedKmh = carRb.velocity.magnitude * 3.6f;

        //속도 비율 (0~1)로 목표 FOV 계산
        float t = Mathf.Clamp01(speedKmh / maxSpeedKmh);
        float targetFOV = Mathf.Lerp(baseFOV, maxFOV, t);
        float lerp = changeSpeed * Time.deltaTime;

        //정면은 vcam이 FOV를 쥐고 있어서 vcam을 통해 바꿈
        frontVcam.m_Lens.FieldOfView = Mathf.Lerp(frontVcam.m_Lens.FieldOfView, targetFOV, lerp);

        //좌우는 일반 카메라라 직접 바꿈
        leftCam.fieldOfView = Mathf.Lerp(leftCam.fieldOfView, targetFOV, lerp);

        rightCam.fieldOfView = Mathf.Lerp(rightCam.fieldOfView, targetFOV, lerp);
    }
}
