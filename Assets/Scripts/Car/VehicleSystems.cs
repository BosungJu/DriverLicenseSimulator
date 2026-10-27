using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class VehicleSystems : MonoBehaviour
{
    [Header("연결")]
    public CarController carController;      // 핸들 조향값 읽기용 (깜빡이 자동취소). 없으면 자동취소 안 함
    [Header("Input System 입력")]
    public InputActionReference engineAction;
    public InputActionReference hazardAction;
    public InputActionReference leftSignalAction;
    public InputActionReference rightSignalAction;
    public InputActionReference headlightAction; // 누를 때마다 꺼짐 → 전조등 → 상향등 → 꺼짐 순환
    public InputActionReference hornAction;      // 누르고 있는 동안 경음기
    public InputActionReference seatBeltAction;  // 누를 때마다 안전벨트 착용/해제

    [Header("경음기 (선택)")]
    public AudioSource hornAudio;           // Loop 켜진 클립 권장

    [Header("UI 표시등 (GameObject)")]
    public GameObject engineIndicator;
    public GameObject leftSignal;
    public GameObject rightSignal;
    public GameObject headlightIndicator;
    public GameObject highBeamIndicator;

    [Header("실제 조명 (선택)")]
    public Light[] headlights;              // 전조등 스팟라이트들
    public Light[] highBeams;               // 상향등 스팟라이트들

    [Header("깜빡임")]
    public float blinkInterval = 0.4f;

    [Header("깜빡이 자동취소")]
    [Tooltip("켜진 깜빡이 방향으로 이 값(조향 입력 0~1) 이상 꺾여야 자동취소가 준비된다.")]
    [SerializeField, Range(0f, 1f)] float signalArmSteerThreshold = 0.5f;
    [Tooltip("자동취소 준비 조건이 이 시간(초) 이상 유지돼야 한다. 자이로 값이 순간적으로 튀는 것을 무시한다.")]
    [SerializeField, Min(0f)] float signalArmHoldSeconds = 0.3f;
    [Tooltip("준비된 뒤 조향 입력이 이 값 이하로 돌아오면 중립으로 보고 깜빡이를 끈다.")]
    [SerializeField, Range(0f, 1f)] float signalCancelSteerThreshold = 0.05f;

    // ── 상태 ──
    bool engineOn, hazardOn, leftOn, rightOn, headlightOn, highBeamOn;
    bool seatBeltOn, hornOn;
    float blinkTimer; bool blinkState;
    bool signalArmed;   // 깜빡이 자동취소용 (핸들이 깜빡이 방향으로 충분히 꺾였는지)
    float signalArmHoldTimer; // 깜빡이 방향으로 기준 이상 꺾인 채 유지된 시간
    void OnEnable()
    {
        SetInputEnabled(true);
    }

    void OnDisable()
    {
        SetInputEnabled(false);

        // 누른 채로 비활성화되면 루프 경음기가 계속 울리지 않도록 정리
        hornOn = false;
        if (hornAudio != null) hornAudio.Stop();
    }

    void SetInputEnabled(bool enabled)
    {
        InputActionReference[] references =
        {
            engineAction,
            hazardAction,
            leftSignalAction,
            rightSignalAction,
            headlightAction,
            hornAction,
            seatBeltAction
        };

        foreach (InputActionReference reference in references)
        {
            if (reference == null || reference.action == null)
            {
                continue;
            }

            if (enabled)
            {
                reference.action.Enable();
            }
            else
            {
                reference.action.Disable();
            }
        }
    }

    // WasPressedThisFrame은 Interaction을 무시하므로, 같은 키를 Tap/Hold로 나눠 쓰려면 Performed 기준으로 읽어야 한다
    bool WasPerformed(InputActionReference reference)
    {
        return reference != null
            && reference.action != null
            && reference.action.WasPerformedThisFrame();
    }
    void Update()
    {
        HandleInput();
        UpdateHorn();
        UpdateBlink();
        UpdateSignalAutoCancel();
        UpdateVisuals();
    }

    void HandleInput()
    {
        // 시동
        if (WasPerformed(engineAction))
        {
            engineOn = !engineOn;

            if (!engineOn)
            {
                TurnAllOff();
            }

            Debug.Log($"[VehicleSystems] Engine {(engineOn ? "ON" : "OFF")}", this);
        }

        // 비상등
        if (WasPerformed(hazardAction))
        {
            hazardOn = !hazardOn;

            if (hazardOn)
            {
                leftOn = false;
                rightOn = false;
            }
        }

        // 안전벨트 (시동과 무관)
        if (WasPerformed(seatBeltAction))
        {
            seatBeltOn = !seatBeltOn;

            Debug.Log($"[VehicleSystems] SeatBelt {(seatBeltOn ? "ON" : "OFF")}", this);
        }

        // 시동이 꺼져 있으면 아래 조작은 무시
        if (!engineOn)
        {
            return;
        }

        // 좌측 방향지시등
        if (WasPerformed(leftSignalAction))
        {
            leftOn = !leftOn;
            rightOn = false;
            hazardOn = false;
            ResetSignalAutoCancel();
        }

        // 우측 방향지시등
        if (WasPerformed(rightSignalAction))
        {
            rightOn = !rightOn;
            leftOn = false;
            hazardOn = false;
            ResetSignalAutoCancel();
        }

        // 전조등
        if (WasPerformed(headlightAction))
        {
            CycleHeadlightMode();
        }
    }

    // 꺼짐 → 전조등 → 상향등 → 꺼짐. 상향등 단계에서도 전조등은 켜진 상태로 유지
    void CycleHeadlightMode()
    {
        if (!headlightOn)
        {
            headlightOn = true;
            highBeamOn = false;
        }
        else if (!highBeamOn)
        {
            highBeamOn = true;
        }
        else
        {
            headlightOn = false;
            highBeamOn = false;
        }
    }

    void TurnAllOff()
    {
        //시동 끌 때: 비상등 제외하고 나머지 다 끔
        leftOn = rightOn = headlightOn = highBeamOn = false;
    }

    // 경음기는 시동과 무관하게 누르고 있는 동안만 울림
    void UpdateHorn()
    {
        bool isHornPressed = hornAction != null
            && hornAction.action != null
            && hornAction.action.IsPressed();

        if (isHornPressed == hornOn)
        {
            return;
        }

        hornOn = isHornPressed;

        if (hornAudio == null)
        {
            return;
        }

        if (hornOn)
        {
            hornAudio.Play();
        }
        else
        {
            hornAudio.Stop();
        }
    }

    void UpdateBlink()
    {
        blinkTimer += Time.deltaTime;
        if (blinkTimer >= blinkInterval) { blinkTimer = 0f; blinkState = !blinkState; }
    }

    void UpdateSignalAutoCancel()
    {
        if (!leftOn && !rightOn) return;                 // 깜빡이 켜졌을 때만 검사
        if (carController == null) return;               // 조향값을 읽을 곳이 없으면 자동취소 안 함

        // 조향 입력은 음수가 좌회전, 양수가 우회전이다. 깜빡이 방향으로 꺾인 양만 본다
        float steer = carController.SteeringInput;
        float signalDirectionSteer = leftOn ? -steer : steer;

        if (!signalArmed)
        {
            // 살짝 꺾거나 반대로 흔들린 것은 무시하고, 기준 이상으로 일정 시간 꺾여 있어야 준비한다
            if (signalDirectionSteer >= signalArmSteerThreshold)
            {
                signalArmHoldTimer += Time.deltaTime;
                signalArmed = signalArmHoldTimer >= signalArmHoldSeconds;
            }
            else
            {
                signalArmHoldTimer = 0f;
            }

            return;
        }

        // 준비된 뒤 핸들이 중립으로 돌아오면 자동 끔
        if (Mathf.Abs(steer) <= signalCancelSteerThreshold)
        {
            leftOn = rightOn = false;
            ResetSignalAutoCancel();
        }
    }

    void ResetSignalAutoCancel()
    {
        signalArmed = false;
        signalArmHoldTimer = 0f;
    }

    void UpdateVisuals()
    {
        if (engineIndicator) engineIndicator.SetActive(engineOn);

        // 깜빡이/비상등은 blinkState일 때만 켜서 '깜빡'이게
        bool leftBlink = (leftOn || hazardOn) && blinkState;
        bool rightBlink = (rightOn || hazardOn) && blinkState;
        if (leftSignal) leftSignal.SetActive(leftBlink);
        if (rightSignal) rightSignal.SetActive(rightBlink);

        if (headlightIndicator) headlightIndicator.SetActive(headlightOn);
        if (highBeamIndicator) highBeamIndicator.SetActive(highBeamOn);

        SetLights(headlights, headlightOn || highBeamOn); // 상향등이면 전조등도 켜짐
        SetLights(highBeams, highBeamOn);
    }

    void SetLights(Light[] lights, bool on)
    {
        if (lights == null) return;
        foreach (var l in lights) if (l) l.enabled = on;
    }

    public bool EngineOn => engineOn;   // CarController에서 읽기용
    public bool HeadlightOn => headlightOn;
    public bool HighBeamOn => highBeamOn;
    public bool SeatBeltOn => seatBeltOn;
    public bool HornOn => hornOn;
    public bool HazardOn => hazardOn;
    public bool LeftSignalOn => leftOn;     // 깜빡임 위상과 무관한 켜짐 상태
    public bool RightSignalOn => rightOn;
    public bool LeftSignalVisible => (leftOn || hazardOn) && blinkState;
    public bool RightSignalVisible => (rightOn || hazardOn) && blinkState;
}
