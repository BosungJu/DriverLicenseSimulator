using UnityEngine;

public class WiperController : MonoBehaviour
{
    [Header("회전 중심")]
    public Transform leftPivot;
    public Transform rightPivot;

    [Header("왕복 설정")]
    [Range(0f, 150f)]
    public float sweepAngle = 150f;

    [Min(0.01f)]
    public float sweepSpeed = 2.5f;

    [Header("올라가는 방향: 1 또는 -1")]
    public float leftDirection = 1f;
    public float rightDirection = -1f;

    private bool isWiping;
    private float phase;

    private Quaternion leftStartRotation;
    private Quaternion rightStartRotation;

    private const float FullCycle = Mathf.PI * 2f;

    void Start()
    {
        if (leftPivot != null)
            leftStartRotation = leftPivot.localRotation;

        if (rightPivot != null)
            rightStartRotation = rightPivot.localRotation;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.V))
            isWiping = !isWiping;

        // 끈 경우에도 진행 중인 왕복은 끝까지 마무리
        if (isWiping || phase > 0f)
        {
            phase += Time.deltaTime * Mathf.Max(0.01f, sweepSpeed);

            if (phase >= FullCycle)
            {
                phase = isWiping
                    ? Mathf.Repeat(phase, FullCycle)
                    : 0f;
            }
        }

        // 정지 0° → 최대 각도 → 정지 0°
        float angle =
            (1f - Mathf.Cos(phase)) * 0.5f * sweepAngle;

        if (leftPivot != null)
        {
            float direction = leftDirection >= 0f ? 1f : -1f;

            leftPivot.localRotation =
                leftStartRotation *
                Quaternion.AngleAxis(
                    angle * direction,
                    Vector3.forward
                );
        }

        if (rightPivot != null)
        {
            float direction = rightDirection >= 0f ? 1f : -1f;

            rightPivot.localRotation =
                rightStartRotation *
                Quaternion.AngleAxis(
                    angle * direction,
                    Vector3.forward
                );
        }
    }
}