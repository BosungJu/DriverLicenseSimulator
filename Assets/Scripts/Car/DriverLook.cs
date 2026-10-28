using UnityEngine;
using UnityEngine.InputSystem;

// 패드 우측 스틱으로 운전자 시점(DriverEye)을 돌린다.
// 스틱을 기울인 만큼의 속도로 최대 각도까지 고개를 돌리고, 놓으면 그 각도를 유지한다 (숄더 체크용).
// DriverEye 회전은 CabShake가 매 프레임 덮어쓰므로, 실제 적용은 CabShake가 GetLookRotation()으로 합성한다.
public class DriverLook : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] InputActionReference lookAction; // Car/Look (Vector2, <Gamepad>/rightStick)

    [Header("Tuning")]
    [SerializeField, Range(0f, 0.5f)] float deadZone = 0.1f; // 스틱 영점 근처 흔들림을 무시할 범위
    [SerializeField, Range(0f, 180f)] float maxYawAngle = 90f; // 좌우로 돌릴 수 있는 최대 각도
    [SerializeField, Range(0f, 89f)] float maxPitchAngle = 30f; // 위아래로 돌릴 수 있는 최대 각도
    [SerializeField, Min(0f)] float turnSpeed = 120f; // 스틱을 끝까지 기울였을 때 고개를 돌리는 속도 (도/초)
    [SerializeField, Min(0f)] float followSpeed = 8f; // 목표 각도를 따라가는 부드러움 정도
    [SerializeField] bool invertPitch;

    // 스틱 입력으로 누적한 목표 각도 (도). 스틱을 놓아도 유지된다.
    float targetYaw;
    float targetPitch;

    // 현재 시점 각도 (도). 양수 yaw는 오른쪽, 양수 pitch는 위쪽
    public float YawAngle { get; private set; }
    public float PitchAngle { get; private set; }

    bool HasAction => lookAction != null && lookAction.action != null;

    void OnEnable()
    {
        if (!HasAction)
        {
            Debug.LogWarning("[DriverLook] Look Action이 연결되지 않아 시점은 정면으로 고정됩니다.", this);
            return;
        }

        lookAction.action.Enable();
    }

    void Update()
    {
        Vector2 lookInput = ReadLookInput();

        float pitchInput = invertPitch ? -lookInput.y : lookInput.y;
        float turnDelta = turnSpeed * Time.deltaTime;

        targetYaw = Mathf.Clamp(targetYaw + lookInput.x * turnDelta, -maxYawAngle, maxYawAngle);
        targetPitch = Mathf.Clamp(targetPitch + pitchInput * turnDelta, -maxPitchAngle, maxPitchAngle);

        float lerp = followSpeed * Time.deltaTime;

        YawAngle = Mathf.Lerp(YawAngle, targetYaw, lerp);
        PitchAngle = Mathf.Lerp(PitchAngle, targetPitch, lerp);
    }

    void OnDisable()
    {
        if (HasAction)
        {
            lookAction.action.Disable();
        }

        // 다시 켜졌을 때 이전 시점이 남아 있지 않도록 정면으로 초기화
        targetYaw = 0f;
        targetPitch = 0f;
        YawAngle = 0f;
        PitchAngle = 0f;
    }

    // 기본 로컬 회전에 시점 회전을 합성한다.
    // yaw는 차량 위쪽 축 기준으로 바깥에서 돌려야 기본 내려다보는 각도 때문에 화면이 기울지 않는다.
    public Quaternion GetLookRotation(Quaternion baseLocalRotation)
    {
        return Quaternion.Euler(0f, YawAngle, 0f) * baseLocalRotation * Quaternion.Euler(-PitchAngle, 0f, 0f);
    }

    Vector2 ReadLookInput()
    {
        if (!HasAction)
        {
            return Vector2.zero;
        }

        Vector2 lookInput = lookAction.action.ReadValue<Vector2>();
        return lookInput.magnitude < deadZone ? Vector2.zero : lookInput;
    }
}
