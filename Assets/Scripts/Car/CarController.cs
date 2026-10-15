using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.InputSystem;

// 위에서부터 P → R → N → D 순서 (D-pad 위로 올리면 P 쪽, 내리면 D 쪽)
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
    public InputActionReference handleAction; // 핸들 (Vector2의 x축 사용, -1 ~ 1)
    public InputActionReference gearAction; // 기어 (Dpad의 위/아래 사용)
    public InputActionReference parkingBrakeAction; // 주차 브레이크 (Button, 토글)

    const float GearShiftInputThreshold = 0.5f; // D-pad 위/아래가 눌렸다고 판단할 최소 입력값
    const float FullBrakeVelocityDamping = 0.98f; // 브레이크를 끝까지 밟았을 때 물리 스텝마다 곱하는 감속 비율

    CarMoveInput moveInput = new CarMoveInput();

    float currentSteerAngle; // 현재 적용 중인 조향각
    float currentMotor; // 현재 적용 중인 모터 토크

    VehicleGear currentGear = VehicleGear.Park;
    bool parkingBrakeOn;
    int lastGearShiftDirection; // 직전 프레임의 D-pad 방향. 누르고 있는 동안 한 칸만 이동하도록 비교용

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
            handleAction,
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
        if (HasAction(excelAction) && HasAction(brakeAction) && HasAction(handleAction))
        {
            return;
        }

        Debug.LogWarning("[CarController] Excel/Brake/Handle 입력 액션이 연결되지 않아 해당 입력은 0으로 처리됩니다.", this);
    }

    static bool HasAction(InputActionReference reference)
    {
        return reference != null && reference.action != null;
    }

    void GetInput()
    {
        // Handle 액션은 Vector2(leftStick)로 설정되어 있어 x축만 조향에 사용
        moveInput.Steering = HasAction(handleAction) ? handleAction.action.ReadValue<Vector2>().x : 0f;

        moveInput.Throttle = HasAction(excelAction) ? Mathf.Clamp01(excelAction.action.ReadValue<float>()) : 0f;

        moveInput.Brake = HasAction(brakeAction) ? Mathf.Clamp01(brakeAction.action.ReadValue<float>()) : 0f;

        InputSampleFixedTime = Time.fixedTime;
        HasInputSample = true;
    }

    void HandleGearInput()
    {
        int shiftDirection = ReadGearShiftDirection();
        bool isNewShift = shiftDirection != 0 && shiftDirection != lastGearShiftDirection;

        lastGearShiftDirection = shiftDirection;

        if (!isNewShift)
        {
            return;
        }

        ShiftGear(shiftDirection);
    }

    // D-pad 위 = -1 (P 쪽), 아래 = 1 (D 쪽), 중립 = 0
    int ReadGearShiftDirection()
    {
        if (!HasAction(gearAction))
        {
            return 0;
        }

        float verticalInput = gearAction.action.ReadValue<Vector2>().y;

        if (verticalInput >= GearShiftInputThreshold)
        {
            return -1;
        }

        if (verticalInput <= -GearShiftInputThreshold)
        {
            return 1;
        }

        return 0;
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
        if (!HasAction(parkingBrakeAction) || !parkingBrakeAction.action.WasPressedThisFrame())
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
