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
    [Tooltip("가속 페달을 끝까지 밟았을 때 뒷바퀴에 거는 최대 구동 토크")]
    public float motorForce = 1500f;
    [Tooltip("브레이크 페달을 끝까지 밟았을 때의 제동 토크. P 기어와 주차 브레이크도 이 값으로 뒷바퀴를 잡는다")]
    public float brakeForce = 12000f;
    [Tooltip("초당 토크 증가량 (가속 페달을 밟을 때)")]
    public float accelRate = 1200f;
    [Tooltip("초당 토크 감소량 (가속 페달을 뗄 때)")]
    public float releaseRate = 2500f;
    [Tooltip("전진 최고 속도(km/h). 엑셀 강도와 무관한 구동력 차단 기준이며 자동변속 속도도 이 값에 비례한다. 관성이나 내리막에 의한 초과 속도를 강제로 제한하지는 않는다")]
    public float maxSpeedKmh = 110f;

    [Header("Throttle Response")]
    [Tooltip("이 값보다 약한 엑셀 입력은 페달 노이즈로 보고 동력을 전달하지 않는다")]
    [SerializeField, Range(0f, 0.1f)] float throttleDeadZone = 0.02f;
    [Tooltip("데드존을 넘은 가장 약한 엑셀에서도 전달되는 최소 토크 비율(motorForce 기준). 엑셀 강도가 커질수록 1까지 선형으로 늘어난다")]
    [SerializeField, Range(0f, 1f)] float minimumDriveTorqueRatio = 0.3f;
    [Tooltip("전진/후진 최고 속도에 이만큼(km/h) 가까워지면 구동 토크를 서서히 줄인다")]
    [SerializeField, Min(0.1f)] float throttleSpeedFadeKmh = 2f;
    [Tooltip("브레이크를 끝까지 밟았을 때 물리 스텝마다 속도에 곱하는 비율. 작을수록 빨리 선다 (1이면 추가 감속 없음)")]
    [SerializeField, Range(0.9f, 1f)] float fullBrakeVelocityDamping = 0.98f;

    [Header("Steering Settings")]
    [Tooltip("저속에서의 최대 조향각 (도)")]
    public float maxSteerAngle = 32f;
    [Tooltip("고속에서의 최소 조향각 (도)")]
    public float minSteerAngle = 6f;
    [Tooltip("핸들 회전 속도 (초당 각도)")]
    public float steerSpeed = 80f;
    [Tooltip("이 속도(m/s) 이상이면 최소 조향각을 적용한다")]
    public float fullSteerSpeedThreshold = 22f;

    [Header("Gear Settings")]
    [Tooltip("기어 버튼이 눌렸다고 판단할 최소 입력값")]
    [SerializeField, Range(0.1f, 1f)] float gearShiftInputThreshold = 0.5f;
    [Tooltip("이 속도(m/s) 이하일 때만 변속을 허용한다. 물리 연산의 미세한 움직임은 정지로 간주하기 위한 값")]
    [SerializeField, Min(0f)] float gearShiftMaxSpeedMetersPerSecond = 0.05f;

    [Header("Automatic Transmission")]
    [Tooltip("D에서 1~4단 자동변속에 걸리는 시간(초). 변속 중에는 구동 토크가 줄어든다")]
    [SerializeField, Min(0f)] float automaticShiftDurationSeconds = 0.35f;
    [Tooltip("후진 최고 속도(km/h). 후진은 자동변속 없이 고정 기어를 사용한다")]
    [SerializeField, Min(0f)] float maxReverseSpeedKmh = 20f;

    [Header("Physics")]
    [Tooltip("Rigidbody 기준 무게중심 위치(로컬 좌표). 낮을수록 잘 전복되지 않는다. 플레이 중 바꾸면 바로 적용된다")]
    [SerializeField] Vector3 centerOfMassOffset = new Vector3(0f, -0.7f, 0f);

    [Header("Systems")]
    public VehicleSystems systems;

    [Header("Input System 입력")]
    public InputActionReference excelAction; // 가속 페달 (Axis, 0 ~ 1)
    public InputActionReference brakeAction; // 브레이크 페달 (Axis, 0 ~ 1)
    public InputActionReference gearAction; // 기어 (1D Axis: LB = 1, RB = -1)
    public InputActionReference parkingBrakeAction; // 주차 브레이크 (Button, 토글. Hold 등 Interaction 반영)
    public GyroScope gyroScope; // 핸들 (-1 ~ 1). 조향은 항상 GyroScope가 가공한 값만 사용. 비어 있으면 같은 오브젝트에서 찾는다

    CarMoveInput moveInput = new CarMoveInput();

    float currentSteerAngle; // 현재 적용 중인 조향각
    float currentMotor; // 현재 적용 중인 모터 토크
    private readonly AutomaticTransmission automaticTransmission = new AutomaticTransmission();

    VehicleGear currentGear = VehicleGear.Park;
    bool parkingBrakeOn;
    int lastGearShiftDirection; // 직전 프레임의 기어 입력 방향. 누르고 있는 동안 한 칸만 이동하도록 비교용

    Rigidbody rb;

    public VehicleGear CurrentGear => currentGear;
    public int CurrentForwardGear => currentGear == VehicleGear.Drive ? automaticTransmission.CurrentGear : 0;
    public bool IsAutomaticShifting => currentGear == VehicleGear.Drive && automaticTransmission.IsShifting;
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
        automaticTransmission.Reset();
        ResetDriveTorque();
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.centerOfMass = centerOfMassOffset;

        if (gyroScope == null)
        {
            TryGetComponent(out gyroScope);
        }

        WarnMissingDriveActions();
    }

    // 플레이 중 Inspector에서 무게중심을 조정하면 바로 반영한다. rb는 Start 이후에만 있다
    void OnValidate()
    {
        if (rb != null)
        {
            rb.centerOfMass = centerOfMassOffset;
        }
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

        if (shiftInput >= gearShiftInputThreshold)
        {
            return 1;
        }

        if (shiftInput <= -gearShiftInputThreshold)
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
        if (rb == null)
        {
            return;
        }

        if (rb.linearVelocity.sqrMagnitude > gearShiftMaxSpeedMetersPerSecond * gearShiftMaxSpeedMetersPerSecond)
        {
            Debug.Log("[CarController] Gear shift rejected: stop the vehicle before shifting.", this);
            return;
        }

        int nextGearIndex = Mathf.Clamp((int)currentGear + shiftDirection, (int)VehicleGear.Park, (int)VehicleGear.Drive);
        VehicleGear nextGear = (VehicleGear)nextGearIndex;

        if (nextGear == currentGear)
        {
            return;
        }

        currentGear = nextGear;

        // 이전 기어의 토크가 새 기어에 남지 않도록 즉시 초기화한다.
        automaticTransmission.Reset();
        ResetDriveTorque();

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

        // 시동 OFF, P/N 또는 주차 브레이크 작동 중에는 구동 토크를 즉시 차단한다.
        if (systems == null || !systems.EngineOn || gearTorqueDirection == 0f || parkingBrakeOn)
        {
            automaticTransmission.Reset();
            ResetDriveTorque();
            return;
        }

        // 선택한 기어 방향으로의 속도. D에서 뒤로 밀리면 음수가 되어 토크가 줄지 않으므로 엑셀로 밀림을 막을 수 있다
        float gearDirectionSpeedKmh = Vector3.Dot(rb.linearVelocity, transform.forward) * gearTorqueDirection * 3.6f;
        if (currentGear == VehicleGear.Drive)
        {
            automaticTransmission.Tick(gearDirectionSpeedKmh, maxSpeedKmh,
                automaticShiftDurationSeconds, Time.fixedDeltaTime);
        }
        else
        {
            automaticTransmission.Reset();
        }

        float targetMotor = CalculateDriveTorque(moveInput.Throttle, gearDirectionSpeedKmh) * gearTorqueDirection;

        // 목표 토크가 커지면 accelRate, 작아지면 releaseRate 적용
        float rate = Mathf.Abs(targetMotor) > Mathf.Abs(currentMotor) ? accelRate : releaseRate;

        currentMotor = Mathf.MoveTowards(currentMotor, targetMotor, rate * Time.fixedDeltaTime);

        rearLeftCollider.motorTorque = currentMotor;
        rearRightCollider.motorTorque = currentMotor;
    }

    void ResetDriveTorque()
    {
        currentMotor = 0f;
        if (rearLeftCollider != null)
        {
            rearLeftCollider.motorTorque = 0f;
        }
        if (rearRightCollider != null)
        {
            rearRightCollider.motorTorque = 0f;
        }
    }

    // 엑셀은 구동력을 조절하고, D의 자동변속 단수는 속도로 결정한다.
    // 최고 속도 근처에서만 토크를 줄이므로 약한 엑셀이 특정 속도의 제한으로 작용하지 않는다.
    // gearDirectionSpeedKmh는 선택한 기어 방향 기준이며, 반대 방향으로 움직이면 음수다
    float CalculateDriveTorque(float throttle, float gearDirectionSpeedKmh)
    {
        float speedLimitKmh = currentGear == VehicleGear.Reverse ? maxReverseSpeedKmh : maxSpeedKmh;
        if (throttle <= 0f || throttle < throttleDeadZone || speedLimitKmh <= 0f)
        {
            return 0f;
        }

        float throttleTorqueRatio = Mathf.Lerp(minimumDriveTorqueRatio, 1f, throttle);

        float speedFadeRatio = Mathf.Clamp01((speedLimitKmh - gearDirectionSpeedKmh) / Mathf.Max(0.1f, throttleSpeedFadeKmh));
        float gearTorqueRatio = currentGear == VehicleGear.Drive ? automaticTransmission.TorqueRatio : 1f;

        return motorForce * throttleTorqueRatio * gearTorqueRatio * speedFadeRatio;
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
            rb.linearVelocity *= Mathf.Lerp(1f, fullBrakeVelocityDamping, moveInput.Brake);
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
