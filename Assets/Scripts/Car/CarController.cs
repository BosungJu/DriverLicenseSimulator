using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.InputSystem;

// P → R → N → D 순서 (LB를 누르면 D 쪽, RB를 누르면 P 쪽으로 한 칸)
public enum VehicleGear
{
    Park,
    Reverse,
    Neutral,
    Drive
}

public class CarController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider frontLeftCollider;
    public WheelCollider frontRightCollider;
    public WheelCollider rearLeftCollider;
    public WheelCollider rearRightCollider;

    [Header("Wheel Meshes")]
    public Transform frontLeftWheel;
    public Transform frontRightWheel;
    public Transform rearLeftWheel;
    public Transform rearRightWheel;

    [Header("Car Settings")]
    public float motorForce = 1500f;
    public float brakeForce = 12000f;
    public float accelRate = 1200f; // 초당 토크 증가량 (가속 페달을 밟을 때)
    public float releaseRate = 2500f; // 초당 토크 감소량 (가속 페달을 뗄 때)
    public float maxSpeedKmh = 110f; // 1단 기어 기준 최고 속도 제한

    [Header("Steering Settings")]
    public float maxSteerAngle = 32f; // 저속에서의 최대 조향각
    public float minSteerAngle = 6f; // 고속에서의 최소 조향각
    public float steerSpeed = 80f; // 핸들 회전 속도 (초당 각도)
    public float fullSteerSpeedThreshold = 22f; // 이 속도(m/s) 이상이면 최소 조향각 적용
    [Header("Systems")]
    public VehicleSystems systems;

    [Header("Input System 입력")]
    public InputActionReference excelAction; // 가속 페달 (Axis, 0 ~ 1)
    public InputActionReference brakeAction; // 브레이크 페달 (Axis, 0 ~ 1)
    public InputActionReference gearAction; // 기어 (1D Axis: LB = 1, RB = -1)
    public InputActionReference parkingBrakeAction; // 주차 브레이크 (Button, 토글. Hold 등 Interaction 반영)
    public GyroScope gyroScope; // 핸들 (-1 ~ 1). 조향은 항상 GyroScope가 가공한 값만 사용. 비어 있으면 같은 오브젝트에서 찾는다

    const float GearShiftInputThreshold = 0.5f; // 기어 버튼이 눌렸다고 판단할 최소 입력값
    const float FullBrakeVelocityDamping = 0.98f; // 브레이크를 끝까지 밟았을 때 물리 스텝마다 곱하는 감속 비율

    CarMoveInput moveInput = new CarMoveInput();

    float currentSteerAngle; // 현재 적용 중인 조향각
    float currentMotor; // 현재 적용 중인 모터 토크

    VehicleGear currentGear = VehicleGear.Park;
    bool parkingBrakeOn;
    int lastGearShiftDirection; // 직전 프레임의 기어 입력 방향. 누르고 있는 동안 한 칸만 이동하도록 비교용

    Rigidbody rb;

    public VehicleGear CurrentGear => currentGear;
    public bool ParkingBrakeOn => parkingBrakeOn;
    public float SteeringInput => moveInput.Steering; // 깜빡이 자동취소 등에서 읽기용
    public float ThrottleInput => moveInput.Throttle; // AI 리포트 기록용 (0 ~ 1)
    public float BrakeInput => moveInput.Brake; // AI 리포트 기록용 (0 ~ 1)
    public bool HasInputSample { get; private set; } // 입력을 한 번이라도 읽었는지
    public float InputSampleFixedTime { get; private set; } // 마지막으로 입력을 읽은 물리 스텝 시각

    void OnEnable()
    {
        SetInputEnabled(true);
    }

    void OnDisable()
    {
        SetInputEnabled(false);
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.centerOfMass = new Vector3(0, -0.7f, 0);

        if (gyroScope == null)
        {
            TryGetComponent(out gyroScope);
        }

        WarnMissingDriveActions();
    }

    // 토글/엣지 입력은 프레임 단위로 읽어야 FixedUpdate에서 눌림을 놓치지 않는다
    void Update()
    {
        HandleGearInput();

        HandleParkingBrakeInput();
    }

    void FixedUpdate()
    {
        GetInput();

        Move();

        Steer();

        Brake();

        UpdateWheels();
    }

    void SetInputEnabled(bool enabled)
    {
        InputActionReference[] references =
        {
            excelAction,
            brakeAction,
            gearAction,
            parkingBrakeAction
        };

        foreach (InputActionReference reference in references)
        {
            if (!HasAction(reference))
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

    void WarnMissingDriveActions()
    {
        if (!HasAction(excelAction) || !HasAction(brakeAction))
        {
            Debug.LogWarning("[CarController] Excel/Brake 입력 액션이 연결되지 않아 해당 입력은 0으로 처리됩니다.", this);
        }

        if (gyroScope == null)
        {
            Debug.LogWarning("[CarController] GyroScope가 없어 조향 입력은 0으로 처리됩니다.", this);
        }
    }

    static bool HasAction(InputActionReference reference)
    {
        return reference != null && reference.action != null;
    }

    void GetInput()
    {
        moveInput.Steering = ReadSteeringInput();

        moveInput.Throttle = HasAction(excelAction) ? Mathf.Clamp01(excelAction.action.ReadValue<float>()) : 0f;

        moveInput.Brake = HasAction(brakeAction) ? Mathf.Clamp01(brakeAction.action.ReadValue<float>()) : 0f;

        InputSampleFixedTime = Time.fixedTime;
        HasInputSample = true;
    }

    // 원본 스틱 값은 쓰지 않고 GyroScope가 가공한 값만 사용. GyroScope가 없거나 꺼져 있으면 직진(0)
    float ReadSteeringInput()
    {
        if (gyroScope == null || !gyroScope.isActiveAndEnabled)
        {
            return 0f;
        }

        return gyroScope.SteeringInput;
    }

    void HandleGearInput()
    {
        int shiftDirection = ReadGearShiftDirection();

        // LB/RB를 동시에 누르면 축 값이 0이 된다. 이때 중립으로 처리하면 한쪽을 뗄 때 다시 변속되므로 직전 방향을 유지
        if (shiftDirection == 0 && IsAnyGearButtonHeld())
        {
            return;
        }

        bool isNewShift = shiftDirection != 0 && shiftDirection != lastGearShiftDirection;

        lastGearShiftDirection = shiftDirection;

        if (!isNewShift)
        {
            return;
        }

        ShiftGear(shiftDirection);
    }

    // LB = 1 (D 쪽), RB = -1 (P 쪽), 중립 또는 동시 입력 = 0
    int ReadGearShiftDirection()
    {
        if (!HasAction(gearAction))
        {
            return 0;
        }

        float shiftInput = gearAction.action.ReadValue<float>();

        if (shiftInput >= GearShiftInputThreshold)
        {
            return 1;
        }

        if (shiftInput <= -GearShiftInputThreshold)
        {
            return -1;
        }

        return 0;
    }

    bool IsAnyGearButtonHeld()
    {
        if (!HasAction(gearAction))
        {
            return false;
        }

        foreach (InputControl gearControl in gearAction.action.controls)
        {
            if (gearControl.IsPressed())
            {
                return true;
            }
        }

        return false;
    }

    void ShiftGear(int shiftDirection)
    {
        int nextGearIndex = Mathf.Clamp((int)currentGear + shiftDirection, (int)VehicleGear.Park, (int)VehicleGear.Drive);
        VehicleGear nextGear = (VehicleGear)nextGearIndex;

        if (nextGear == currentGear)
        {
            return;
        }

        currentGear = nextGear;

        Debug.Log($"[CarController] Gear shifted: {currentGear}", this);
    }

    void HandleParkingBrakeInput()
    {
        // WasPressedThisFrame은 Interaction을 무시하므로 Hold가 끝까지 유지됐을 때만 반응하도록 Performed 기준으로 읽는다
        if (!HasAction(parkingBrakeAction) || !parkingBrakeAction.action.WasPerformedThisFrame())
        {
            return;
        }

        parkingBrakeOn = !parkingBrakeOn;

        Debug.Log($"[CarController] Parking brake: {(parkingBrakeOn ? "ON" : "OFF")}", this);
    }

    // D는 전진, R은 후진, P/N은 구동력 없음
    float GetGearTorqueDirection()
    {
        switch (currentGear)
        {
            case VehicleGear.Drive:
                return 1f;
            case VehicleGear.Reverse:
                return -1f;
            default:
                return 0f;
        }
    }

    void Move()
    {
        float gearTorqueDirection = GetGearTorqueDirection();
        float targetMotor = moveInput.Throttle * motorForce * gearTorqueDirection;

        // 시동이 꺼져 있거나 연결이 없거나 P/N이면 구동 토크 즉시 차단
        if (systems == null || !systems.EngineOn || gearTorqueDirection == 0f)
        {
            currentMotor = 0f;
            rearLeftCollider.motorTorque = 0f;
            rearRightCollider.motorTorque = 0f;
            return;
        }

        // 최고 속도를 넘으면 추가 가속을 제한
        float speedKmh = rb.linearVelocity.magnitude * 3.6f;
        if (speedKmh > maxSpeedKmh)
            targetMotor = 0f;

        // 목표 토크가 커지면 accelRate, 작아지면 releaseRate 적용
        float rate = Mathf.Abs(targetMotor) > Mathf.Abs(currentMotor) ? accelRate : releaseRate;

        currentMotor = Mathf.MoveTowards(currentMotor, targetMotor, rate * Time.fixedDeltaTime);

        rearLeftCollider.motorTorque = currentMotor;
        rearRightCollider.motorTorque = currentMotor;
    }

    void Steer()
    {
        float speed = rb.linearVelocity.magnitude; // 현재 속도 (m/s)

        // 속도가 높을수록 최대 조향각을 낮추기 위한 비율 계산
        float speedFactor = Mathf.Clamp01(speed / fullSteerSpeedThreshold);
        float currentMaxSteer = Mathf.Lerp(maxSteerAngle, minSteerAngle, speedFactor);

        // 목표 조향각까지 steerSpeed에 맞춰 부드럽게 이동
        float targetSteerAngle = moveInput.Steering * currentMaxSteer;

        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle,
            targetSteerAngle,
            steerSpeed * Time.fixedDeltaTime
        );

        frontLeftCollider.steerAngle = currentSteerAngle;
        frontRightCollider.steerAngle = currentSteerAngle;
    }

    void Brake()
    {
        float pedalBrakeTorque = moveInput.Brake * brakeForce;

        // P 기어와 주차 브레이크는 페달과 별개로 구동축(뒷바퀴)을 잡아 둔다
        bool isHoldingRearWheels = parkingBrakeOn || currentGear == VehicleGear.Park;
        float rearBrakeTorque = isHoldingRearWheels ? brakeForce : pedalBrakeTorque;

        frontLeftCollider.brakeTorque = pedalBrakeTorque;
        frontRightCollider.brakeTorque = pedalBrakeTorque;
        rearLeftCollider.brakeTorque = rearBrakeTorque;
        rearRightCollider.brakeTorque = rearBrakeTorque;

        if (moveInput.IsBraking)
        {
            // 밟은 정도에 비례해 감속 (끝까지 밟으면 기존과 같은 0.98)
            rb.linearVelocity *= Mathf.Lerp(1f, FullBrakeVelocityDamping, moveInput.Brake);
        }
    }

    void UpdateWheels()
    {
        UpdateSingleWheel(frontLeftCollider, frontLeftWheel);

        UpdateSingleWheel(frontRightCollider, frontRightWheel);

        UpdateSingleWheel(rearLeftCollider, rearLeftWheel);

        UpdateSingleWheel(rearRightCollider, rearRightWheel);
    }

    void UpdateSingleWheel(WheelCollider collider, Transform wheel)
    {
        Vector3 pos;

        Quaternion rot;

        collider.GetWorldPose(out pos, out rot);

        wheel.position = pos;

        wheel.rotation = rot;
    }
}
