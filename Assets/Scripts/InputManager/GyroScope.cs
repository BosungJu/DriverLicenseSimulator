using UnityEngine;
using UnityEngine.InputSystem;

// 게임패드 자이로 값을 조향 입력(-1 ~ 1)으로 제공한다.
// PC의 Input System은 게임패드 자이로를 직접 노출하지 않으므로, Steam Input의 "Gyro → Joystick"처럼
// 자이로를 축 입력으로 변환해 주는 매핑을 전제로 한다. 읽을 축은 steeringAction 바인딩으로 지정한다.
// 차량 오브젝트에 붙이고 CarController가 이 컴포넌트를 참조해 SteeringInput을 읽는 용도.
public class GyroScope : MonoBehaviour
{
    [Header("Input")]
    [SerializeField]
    InputAction steeringAction = new InputAction(
        "GyroSteering",
        InputActionType.Value,
        "<Gamepad>/rightStick/x",
        expectedControlType: "Axis"
    );

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
    public bool IsDeviceConnected => steeringAction.controls.Count > 0;

    void OnEnable()
    {
        steeringAction.Enable();
    }

    void Update()
    {
        RawInput = steeringAction.ReadValue<float>();
        SteeringInput = CalculateSteeringInput(RawInput);
    }

    void OnDisable()
    {
        steeringAction.Disable();

        // 비활성화된 동안 마지막 조향 값이 남아 차량이 계속 꺾이지 않도록 초기화
        RawInput = 0f;
        SteeringInput = 0f;
    }

    // 현재 패드 자세를 직진(0)으로 맞춘다.
    public void Recenter()
    {
        centerOffset = steeringAction.ReadValue<float>();

        Debug.Log($"[GyroScope] Recentered. offset: {centerOffset:F3}", this);
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
