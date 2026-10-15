using UnityEngine;
using UnityEngine.InputSystem;

// 게임패드 자이로 값을 조향 입력(-1 ~ 1)으로 제공한다.
// PC의 Input System은 게임패드 자이로를 직접 노출하지 않으므로, 외부 프로그램이 자이로 센서 값을
// 패드 L 스틱으로 변환해 준다는 전제로 InputSystem_Actions의 Car/Handle_GyroScope 액션(Vector2)의 x축을 읽는다.
// 차량 오브젝트(CarController와 같은 오브젝트)에 붙이면 CarController가 SteeringInput을 조향에 사용한다.
public class GyroScope : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] InputActionReference steeringAction; // Car/Handle_GyroScope (Vector2의 x축 사용)

    [Header("Tuning")]
    [SerializeField, Range(0f, 0.5f)] float deadZone = 0.05f; // 영점 근처 흔들림을 무시할 범위
    [SerializeField, Min(0f)] float sensitivity = 1f;
    [SerializeField] bool invert;

    float centerOffset; // Recenter() 시점의 입력값. 이 값을 0으로 간주한다.

    // 보정 전 원본 축 값
    public float RawInput { get; private set; }

    // 영점, 데드존, 감도, 반전을 적용한 조향 입력 (-1 ~ 1)
    public float SteeringInput { get; private set; }

    // 바인딩에 해당하는 장치가 연결되어 있는지
    public bool IsDeviceConnected => HasAction && steeringAction.action.controls.Count > 0;

    bool HasAction => steeringAction != null && steeringAction.action != null;

    void OnEnable()
    {
        if (!HasAction)
        {
            Debug.LogWarning("[GyroScope] Steering Action이 연결되지 않아 조향 입력은 0으로 처리됩니다.", this);
            return;
        }

        steeringAction.action.Enable();
    }

    void Update()
    {
        RawInput = ReadRawInput();
        SteeringInput = CalculateSteeringInput(RawInput);
    }

    // 액션은 CarController의 handleAction과 공유하므로 여기서 Disable하지 않는다.
    // (자이로가 꺼졌을 때 CarController가 handleAction으로 대체 조향할 수 있어야 한다)
    void OnDisable()
    {
        // 비활성화된 동안 마지막 조향 값이 남아 차량이 계속 꺾이지 않도록 초기화
        RawInput = 0f;
        SteeringInput = 0f;
    }

    // 현재 패드 자세를 직진(0)으로 맞춘다.
    public void Recenter()
    {
        centerOffset = ReadRawInput();

        Debug.Log($"[GyroScope] Recentered. offset: {centerOffset:F3}", this);
    }

    float ReadRawInput()
    {
        return HasAction ? steeringAction.action.ReadValue<Vector2>().x : 0f;
    }

    float CalculateSteeringInput(float rawInput)
    {
        float centeredInput = rawInput - centerOffset;
        float centeredMagnitude = Mathf.Abs(centeredInput);

        if (centeredMagnitude < deadZone)
        {
            return 0f;
        }

        // 데드존 경계에서 값이 튀지 않도록 남은 구간을 0 ~ 1로 다시 매핑
        float rescaledMagnitude = (centeredMagnitude - deadZone) / (1f - deadZone);
        float steeringInput = Mathf.Sign(centeredInput) * rescaledMagnitude * sensitivity;

        if (invert)
        {
            steeringInput = -steeringInput;
        }

        return Mathf.Clamp(steeringInput, -1f, 1f);
    }
}
