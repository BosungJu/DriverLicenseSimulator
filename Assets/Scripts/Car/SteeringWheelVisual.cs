using UnityEngine;

public class SteeringWheelVisual : MonoBehaviour
{
    [Header("Vehicle")]
    public CarController carController;

    [Header("Steering Wheel")]
    [Min(0f)]
    public float maxRotationDegrees = 540f;

    public bool reverseDirection = true;

    [Header("Rotation Speed")]
    public float wheelRotationSpeed = 240f;

    [Header("Current Angle - Runtime")]
    [SerializeField]
    private float currentAngle;

    private Quaternion initialRotation;

    void Awake()
    {
        // 실행 시작 시 핸들의 중앙 자세 저장
        initialRotation = transform.localRotation;
    }

    void LateUpdate()
    {
        if (carController == null)
        {
            return;
        }

        // 실차처럼 운전자가 돌린 만큼 핸들이 돌아가도록 조향 입력(-1~1)을 그대로 쓴다.
        // 앞바퀴 각도는 속도에 따라 줄어들므로 기준으로 쓰면 고속에서 핸들이 거의 돌지 않는다
        float steeringRatio = Mathf.Clamp(
            carController.SteeringInput,
            -1f,
            1f
        );

        // 핸들의 좌우 최대 회전각
        float limit = Mathf.Max(0f, maxRotationDegrees);

        // 회전 방향 반전 여부
        float direction = reverseDirection ? -1f : 1f;

        // 목표 핸들 각도
        float targetAngle = Mathf.Clamp(
            steeringRatio * limit * direction,
            -limit,
            limit
        );

        // 핸들이 목표 각도까지 천천히 회전하도록 제한
        currentAngle = Mathf.MoveTowards(
            currentAngle,
            targetAngle,
            wheelRotationSpeed * Time.deltaTime
        );

        // 초기 자세를 기준으로 로컬 Z축 회전
        transform.localRotation =
            initialRotation *
            Quaternion.AngleAxis(
                currentAngle,
                Vector3.forward
            );
    }
}