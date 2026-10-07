using UnityEngine;

public class LightLeverVisual : MonoBehaviour
{
    [Header("조명 상태를 관리하는 스크립트")]
    public VehicleSystems vehicleSystems;

    [Header("움직일 오브젝트")]
    public Transform lightDialPivot;
    public Transform lightLeverPivot;

    [Header("전조등 ON일 때 다이얼 회전량")]
    public Vector3 dialOnRotation = new Vector3(0f, 90f, 0f);

    [Header("상향등 ON일 때 레버 회전량")]
    public Vector3 highBeamRotation = new Vector3(15f, 0f, 0f);

    [Header("회전 속도: 초당 각도")]
    [Min(1f)]
    public float rotationSpeed = 240f;

    private Quaternion dialOffRotation;
    private Quaternion leverOffRotation;

    void Start()
    {
        // 실행 전 배치한 회전을 OFF 위치로 저장
        if (lightDialPivot != null)
            dialOffRotation = lightDialPivot.localRotation;

        if (lightLeverPivot != null)
            leverOffRotation = lightLeverPivot.localRotation;
    }

    void LateUpdate()
    {
        if (vehicleSystems == null)
            return;

        float step = rotationSpeed * Time.deltaTime;

        if (lightDialPivot != null)
        {
            Quaternion target = dialOffRotation;

            if (vehicleSystems.HeadlightOn)
                target = dialOffRotation
                    * Quaternion.Euler(dialOnRotation);

            lightDialPivot.localRotation =
                Quaternion.RotateTowards(
                    lightDialPivot.localRotation,
                    target,
                    step
                );
        }

        if (lightLeverPivot != null)
        {
            Quaternion target = leverOffRotation;

            if (vehicleSystems.HighBeamOn)
                target = leverOffRotation
                    * Quaternion.Euler(highBeamRotation);

            lightLeverPivot.localRotation =
                Quaternion.RotateTowards(
                    lightLeverPivot.localRotation,
                    target,
                    step
                );
        }
    }
}