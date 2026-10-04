using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cinemachine; //vcam ��� ���

public class SpeedFOV : MonoBehaviour
{
    public Rigidbody carRb; //Car�� Rigidbody
    public CinemachineVirtualCamera frontVcam; //DriverEye�� Vcam (����)
    public Camera leftCam; //LeftCamera
    public Camera rightCam; //RightCamera

    [Header("FOV ����")]
    public float baseFOV = 65f; //���� ��
    public float maxFOV = 78f; //�ְ��� ��
    public float maxSpeedKmh = 110f; //�� �ӵ����� maxFOV ����
    public float changeSpeed = 3f; //FOV ���ϴ� �ε巯��

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float speedKmh = carRb.linearVelocity.magnitude * 3.6f;

        //�ӵ� ���� (0~1)�� ��ǥ FOV ���
        float t = Mathf.Clamp01(speedKmh / maxSpeedKmh);
        float targetFOV = Mathf.Lerp(baseFOV, maxFOV, t);
        float lerp = changeSpeed * Time.deltaTime;

        //������ vcam�� FOV�� ��� �־ vcam�� ���� �ٲ�
        frontVcam.m_Lens.FieldOfView = Mathf.Lerp(frontVcam.m_Lens.FieldOfView, targetFOV, lerp);

        //�¿�� �Ϲ� ī�޶�� ���� �ٲ�
        leftCam.fieldOfView = Mathf.Lerp(leftCam.fieldOfView, targetFOV, lerp);

        rightCam.fieldOfView = Mathf.Lerp(rightCam.fieldOfView, targetFOV, lerp);
    }
}
