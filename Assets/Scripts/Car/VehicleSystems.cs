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
    public InputActionReference headlightAction;
    public InputActionReference highBeamAction;
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

    // ── 상태 ──
    bool engineOn, hazardOn, leftOn, rightOn, headlightOn, highBeamOn;
    bool seatBeltOn, hornOn;
    float blinkTimer; bool blinkState;
    bool signalArmed;   // 깜빡이 자동취소용 (핸들이 한 번 꺾였는지)
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
            highBeamAction,
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

    bool WasPressed(InputActionReference reference)
    {
        return reference != null
            && reference.action != null
            && reference.action.WasPressedThisFrame();
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
        if (WasPressed(engineAction))
        {
            engineOn = !engineOn;

            if (!engineOn)
            {
                TurnAllOff();
            }
        }

        // 비상등
        if (WasPressed(hazardAction))
        {
            hazardOn = !hazardOn;

            if (hazardOn)
            {
                leftOn = false;
                rightOn = false;
            }
        }

        // 안전벨트 (시동과 무관)
        if (WasPressed(seatBeltAction))
        {
            seatBeltOn = !seatBeltOn;
        }

        // 시동이 꺼져 있으면 아래 조작은 무시
        if (!engineOn)
        {
            return;
        }

        // 좌측 방향지시등
        if (WasPressed(leftSignalAction))
        {
            leftOn = !leftOn;
            rightOn = false;
            hazardOn = false;
            signalArmed = false;
        }

        // 우측 방향지시등
        if (WasPressed(rightSignalAction))
        {
            rightOn = !rightOn;
            leftOn = false;
            hazardOn = false;
            signalArmed = false;
        }

        // 전조등
        if (WasPressed(headlightAction))
        {
            headlightOn = !headlightOn;

            if (!headlightOn)
            {
                highBeamOn = false;
            }
        }

        // 전조등이 켜져 있을 때만 상향등 조작
        if (WasPressed(highBeamAction) && headlightOn)
        {
            highBeamOn = !highBeamOn;
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
        float steer = carController.SteeringInput;
        if (Mathf.Abs(steer) > 0.4f) signalArmed = true; // 핸들 꺾임
        if (signalArmed && Mathf.Abs(steer) < 0.1f)      // 다시 중앙 → 자동 끔
        {
            leftOn = rightOn = false; signalArmed = false;
        }
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
}