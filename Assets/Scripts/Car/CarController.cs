using UnityEngine;
using UnityEngine.InputSystem;

// P → R → N → D 순서 (RB를 누르면 D 쪽, LB를 누르면 P 쪽으로 한 칸. 계기판의 P R N D 좌우 배치와 같다)
public enum VehicleGear
{
    Park,
    Reverse,
    Neutral,
    Drive
}

public class CarController : MonoBehaviour
{
    const float MetersPerSecondToKmh = 3.6f;

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
    [Tooltip("가속 페달을 끝까지 밟았을 때 앞바퀴(구동축, 전륜구동)에 거는 바퀴당 최대 구동 토크")]
    public float motorForce = 1500f;
    [Tooltip("브레이크 페달을 끝까지 밟았을 때 앞바퀴 하나의 제동 토크. 타이어 접지 한계를 넘으면 바퀴가 잠겨 오히려 제동거리가 늘어난다. P 기어는 앞바퀴, 주차 브레이크는 뒷바퀴를 이 값으로 잡는다")]
    public float brakeForce = 1000f;
    [Tooltip("페달 제동 시 뒷바퀴 토크 비율(brakeForce 기준). 감속 중에는 하중이 앞으로 쏠려 뒷바퀴가 먼저 잠기고 차가 돌 수 있으므로 앞바퀴보다 약하게 준다")]
    [SerializeField, Range(0f, 1f)] float rearBrakeTorqueRatio = 0.6f;
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

    [Header("Creep & Engine Braking")]
    [Tooltip("엑셀을 밟지 않아도 D/R에서 구동 바퀴에 거는 바퀴당 크리핑 토크. 브레이크만 떼면 차가 천천히 움직이는 자동변속기 동작")]
    [SerializeField, Min(0f)] float creepTorque = 200f;
    [Tooltip("크리핑으로 도달하는 최고 속도(km/h). 엔진 브레이크도 이 속도보다 빠를 때만 걸린다")]
    [SerializeField, Min(0f)] float creepSpeedKmh = 5f;
    [Tooltip("크리핑 최고 속도에 이만큼(km/h) 가까워지면 크리핑 토크를 서서히 줄인다")]
    [SerializeField, Min(0.1f)] float creepSpeedFadeKmh = 2f;
    [Tooltip("엑셀을 뗐을 때 크리핑 속도보다 빠르면 구동 바퀴에 거는 바퀴당 엔진 브레이크 토크")]
    [SerializeField, Min(0f)] float engineBrakeTorque = 150f;

    [Header("Steering Settings")]
    [Tooltip("저속에서의 최대 조향각 (도). 앞축 중앙 기준 값이며, 애커만 조향으로 안쪽 바퀴는 더, 바깥 바퀴는 덜 꺾인다. 실차는 35~40° 정도")]
    public float maxSteerAngle = 38f;
    [Tooltip("고속에서의 최소 조향각 (도)")]
    public float minSteerAngle = 6f;
    [Tooltip("핸들 회전 속도 (초당 각도)")]
    public float steerSpeed = 80f;
    [Tooltip("이 속도(m/s) 이상이면 최소 조향각을 적용한다")]
    public float fullSteerSpeedThreshold = 22f;

    [Header("Gear Settings")]
    [Tooltip("기어 버튼이 눌렸다고 판단할 최소 입력값")]
    [SerializeField, Range(0.1f, 1f)] float gearShiftInputThreshold = 0.5f;
    [Tooltip("이 수평 속도(m/s) 이하면 정지로 본다. P/R이 걸린 변속과 주차 브레이크 완전 고정의 기준. 서스펜션 출렁임(수직 속도)은 포함하지 않는다")]
    [SerializeField, Min(0f)] float gearShiftMaxSpeedMetersPerSecond = 0.3f;
    [Tooltip("P에서 다른 기어로 뺄 때 필요한 최소 브레이크 입력 (실차의 시프트 록)")]
    [SerializeField, Range(0.01f, 1f)] float shiftLockBrakeThreshold = 0.1f;

    [Header("Parking Brake")]
    [Tooltip("주행 중 주차 브레이크를 켰을 때 뒷바퀴에 거는 토크 비율(brakeForce 기준). 한 번에 잠그면 차가 돌 수 있으므로 정지할 때까지는 일부만 건다")]
    [SerializeField, Range(0f, 1f)] float movingParkingBrakeTorqueRatio = 0.5f;

    [Header("Automatic Transmission")]
    [Tooltip("D에서 1~4단 자동변속에 걸리는 시간(초). 변속 중에는 구동 토크가 줄어든다")]
    [SerializeField, Min(0f)] float automaticShiftDurationSeconds = 0.35f;
    [Tooltip("후진 최고 속도(km/h). 후진은 자동변속 없이 고정 기어를 사용한다")]
    [SerializeField, Min(0f)] float maxReverseSpeedKmh = 20f;

    [Header("Physics")]
    [Tooltip("Rigidbody 기준 무게중심 위치(로컬 좌표). 차량 원점은 지면 근처이므로 y는 실차처럼 지면 위(약 0.4~0.55m)로 둔다. 플레이 중 바꾸면 바로 적용된다")]
    [SerializeField] Vector3 centerOfMassOffset = new Vector3(0f, 0.45f, 0f);

    [Header("Systems")]
    public VehicleSystems systems;

    [Header("Input System 입력")]
    public InputActionReference excelAction; // 가속 페달 (Axis, 0 ~ 1)
    public InputActionReference brakeAction; // 브레이크 페달 (Axis, 0 ~ 1)
    public InputActionReference gearAction; // 기어 (1D Axis: RB = 1, LB = -1)
    public InputActionReference parkingBrakeAction; // 주차 브레이크 (Button, 토글. Hold 등 Interaction 반영)
    public GyroScope gyroScope; // 핸들 (-1 ~ 1). 조향은 항상 GyroScope가 가공한 값만 사용. 비어 있으면 같은 오브젝트에서 찾는다

    CarMoveInput moveInput = new CarMoveInput();

    float currentSteerAngle; // 현재 적용 중인 조향각 (앞축 중앙 기준)
    float wheelBaseMeters; // 앞뒤 차축 거리. 애커만 조향 계산용 (Start에서 휠 콜라이더 위치로 계산)
    float halfTrackMeters; // 앞바퀴 좌우 간격의 절반
    float currentMotor; // 현재 적용 중인 모터 토크
    float currentEngineBrakeTorque; // 이번 물리 스텝에 구동 바퀴에 더할 엔진 브레이크 토크
    private readonly AutomaticTransmission automaticTransmission = new AutomaticTransmission();

    VehicleGear currentGear = VehicleGear.Park;
    bool parkingBrakeOn;
    int lastGearShiftDirection; // 직전 프레임의 기어 입력 방향. 누르고 있는 동안 한 칸만 이동하도록 비교용

    Rigidbody rb;

    public VehicleGear CurrentGear => currentGear;
    public int CurrentForwardGear => currentGear == VehicleGear.Drive ? automaticTransmission.CurrentGear : 0;
    public bool IsAutomaticShifting => currentGear == VehicleGear.Drive && automaticTransmission.IsShifting;
    public bool ParkingBrakeOn => parkingBrakeOn;
    public float SteeringInput => moveInput.Steering; // 깜빡이 자동취소, 핸들 비주얼 등에서 읽기용
    public float ThrottleInput => moveInput.Throttle; // AI 리포트 기록용 (0 ~ 1)
    public float BrakeInput => moveInput.Brake; // AI 리포트 기록용 (0 ~ 1)
    public bool HasInputSample { get; private set; } // 입력을 한 번이라도 읽었는지
    public float InputSampleFixedTime { get; private set; } // 마지막으로 입력을 읽은 물리 스텝 시각
    public VehicleGear LastRejectedGear { get; private set; } // 마지막으로 거부된 변속의 목표 기어 (계기판 표시용)
    public float LastGearShiftRejectedTime { get; private set; } = float.NegativeInfinity; // 마지막 변속 거부 시각 (Time.time)
    public bool IsVehicleStopped => rb == null || IsStopped(); // 시동 끄기 허용 판단 등에서 읽기용. 물리 초기화 전에는 정지로 본다

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

        MeasureWheelGeometry();

        CreateTireContactSensors();

        WarnMissingDriveActions();
    }

    // 휠 콜라이더 위치로 휠베이스와 윤거를 구한다. 모델이 바뀌어도 애커만 조향이 실제 바퀴 배치를 따르도록 하기 위함
    void MeasureWheelGeometry()
    {
        Vector3 frontLeftLocal = transform.InverseTransformPoint(frontLeftCollider.transform.position);
        Vector3 frontRightLocal = transform.InverseTransformPoint(frontRightCollider.transform.position);
        Vector3 rearLeftLocal = transform.InverseTransformPoint(rearLeftCollider.transform.position);
        Vector3 rearRightLocal = transform.InverseTransformPoint(rearRightCollider.transform.position);

        float frontAxleZ = (frontLeftLocal.z + frontRightLocal.z) * 0.5f;
        float rearAxleZ = (rearLeftLocal.z + rearRightLocal.z) * 0.5f;

        wheelBaseMeters = Mathf.Abs(frontAxleZ - rearAxleZ);
        halfTrackMeters = Mathf.Abs(frontLeftLocal.x - frontRightLocal.x) * 0.5f;
    }

    // 기능시험 검지선은 타이어가 닿을 때만 감점하므로 바퀴마다 접지면 센서를 둔다 (MapLayerCollisionRelay 참고)
    void CreateTireContactSensors()
    {
        WheelCollider[] wheelColliders = { frontLeftCollider, frontRightCollider, rearLeftCollider, rearRightCollider };

        foreach (WheelCollider wheelCollider in wheelColliders)
        {
            TireContactSensor.Create(wheelCollider, rb);
        }
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
    }

    // Rigidbody 보간으로 차체는 렌더 프레임마다 움직이므로, 바퀴 메시도 렌더 직전에 맞춰야 차체 대비 떨리지 않는다
    void LateUpdate()
    {
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

    static float ReadPedalInput(InputActionReference pedalAction)
    {
        return HasAction(pedalAction) ? Mathf.Clamp01(pedalAction.action.ReadValue<float>()) : 0f;
    }

    void GetInput()
    {
        moveInput.Steering = ReadSteeringInput();

        moveInput.Throttle = ReadPedalInput(excelAction);

        moveInput.Brake = ReadPedalInput(brakeAction);

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

    // 서스펜션 출렁임으로 생기는 수직 속도는 빼고, 노면을 따라 움직이는 속도만 본다
    float GetHorizontalSpeedMetersPerSecond()
    {
        return Vector3.ProjectOnPlane(rb.linearVelocity, transform.up).magnitude;
    }

    bool IsStopped()
    {
        return GetHorizontalSpeedMetersPerSecond() <= gearShiftMaxSpeedMetersPerSecond;
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

    // RB = 1 (D 쪽), LB = -1 (P 쪽), 중립 또는 동시 입력 = 0
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

        int nextGearIndex = Mathf.Clamp((int)currentGear + shiftDirection, (int)VehicleGear.Park, (int)VehicleGear.Drive);
        VehicleGear nextGear = (VehicleGear)nextGearIndex;

        if (nextGear == currentGear)
        {
            return;
        }

        string rejectionReason = GetGearShiftRejectionReason(nextGear);
        if (rejectionReason != null)
        {
            LastRejectedGear = nextGear;
            LastGearShiftRejectedTime = Time.time;

            Debug.Log($"[CarController] Gear shift {currentGear} -> {nextGear} rejected: {rejectionReason}", this);
            return;
        }

        currentGear = nextGear;

        // 이전 기어의 토크가 새 기어에 남지 않도록 즉시 초기화한다.
        automaticTransmission.Reset();
        ResetDriveTorque();

        Debug.Log($"[CarController] Gear shifted: {currentGear}", this);
    }

    // 실차 자동변속기 기준: D↔N은 주행 중에도 가능하고, P/R이 걸린 변속은 정지 상태에서만, P에서 뺄 때는 브레이크를 밟아야 한다.
    // 변속할 수 있으면 null을 반환한다
    string GetGearShiftRejectionReason(VehicleGear nextGear)
    {
        bool involvesParkOrReverse = IsParkOrReverse(currentGear) || IsParkOrReverse(nextGear);
        if (involvesParkOrReverse && !IsStopped())
        {
            return "stop the vehicle before shifting into or out of P/R.";
        }

        bool isLeavingPark = currentGear == VehicleGear.Park;
        if (isLeavingPark && ReadPedalInput(brakeAction) < shiftLockBrakeThreshold)
        {
            return "press the brake pedal to shift out of P.";
        }

        return null;
    }

    static bool IsParkOrReverse(VehicleGear gear)
    {
        return gear == VehicleGear.Park || gear == VehicleGear.Reverse;
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
        currentEngineBrakeTorque = 0f;

        // 시동 OFF, P/N 또는 주차 브레이크 작동 중에는 구동 토크를 즉시 차단한다.
        if (systems == null || !systems.EngineOn || gearTorqueDirection == 0f || parkingBrakeOn)
        {
            automaticTransmission.Reset();
            ResetDriveTorque();
            return;
        }

        // 선택한 기어 방향으로의 속도. D에서 뒤로 밀리면 음수가 되어 토크가 줄지 않으므로 엑셀/크리핑으로 밀림을 막을 수 있다
        float gearDirectionSpeedKmh = Vector3.Dot(rb.linearVelocity, transform.forward) * gearTorqueDirection * MetersPerSecondToKmh;
        if (currentGear == VehicleGear.Drive)
        {
            automaticTransmission.Tick(gearDirectionSpeedKmh, maxSpeedKmh,
                automaticShiftDurationSeconds, Time.fixedDeltaTime);
        }
        else
        {
            automaticTransmission.Reset();
        }

        float creepTorqueMagnitude = CalculateCreepTorque(gearDirectionSpeedKmh);
        float targetMotorMagnitude;
        if (IsThrottleApplied(moveInput.Throttle))
        {
            // 약한 엑셀 토크가 크리핑보다 작으면 엑셀을 밟는 순간 힘이 빠지므로 크리핑을 하한으로 둔다
            targetMotorMagnitude = Mathf.Max(CalculateDriveTorque(moveInput.Throttle, gearDirectionSpeedKmh), creepTorqueMagnitude);
        }
        else
        {
            // 엑셀을 떼면 저속에서는 크리핑, 그보다 빠르면 엔진 브레이크로 감속한다
            targetMotorMagnitude = creepTorqueMagnitude;
            currentEngineBrakeTorque = gearDirectionSpeedKmh > creepSpeedKmh ? engineBrakeTorque : 0f;
        }

        float targetMotor = targetMotorMagnitude * gearTorqueDirection;

        // 목표 토크가 커지면 accelRate, 작아지면 releaseRate 적용
        float rate = Mathf.Abs(targetMotor) > Mathf.Abs(currentMotor) ? accelRate : releaseRate;

        currentMotor = Mathf.MoveTowards(currentMotor, targetMotor, rate * Time.fixedDeltaTime);

        ApplyDriveTorque(currentMotor);
    }

    bool IsThrottleApplied(float throttle)
    {
        return throttle > 0f && throttle >= throttleDeadZone;
    }

    // 쏘나타는 전륜구동이므로 앞바퀴에 구동 토크를 건다
    void ApplyDriveTorque(float motorTorque)
    {
        if (frontLeftCollider != null)
        {
            frontLeftCollider.motorTorque = motorTorque;
        }
        if (frontRightCollider != null)
        {
            frontRightCollider.motorTorque = motorTorque;
        }
    }

    void ResetDriveTorque()
    {
        currentMotor = 0f;
        currentEngineBrakeTorque = 0f;
        ApplyDriveTorque(0f);
    }

    // 엑셀은 구동력을 조절하고, D의 자동변속 단수는 속도로 결정한다.
    // 최고 속도 근처에서만 토크를 줄이므로 약한 엑셀이 특정 속도의 제한으로 작용하지 않는다.
    // gearDirectionSpeedKmh는 선택한 기어 방향 기준이며, 반대 방향으로 움직이면 음수다
    float CalculateDriveTorque(float throttle, float gearDirectionSpeedKmh)
    {
        float speedLimitKmh = currentGear == VehicleGear.Reverse ? maxReverseSpeedKmh : maxSpeedKmh;
        if (speedLimitKmh <= 0f)
        {
            return 0f;
        }

        float throttleTorqueRatio = Mathf.Lerp(minimumDriveTorqueRatio, 1f, throttle);

        float speedFadeRatio = Mathf.Clamp01((speedLimitKmh - gearDirectionSpeedKmh) / Mathf.Max(0.1f, throttleSpeedFadeKmh));
        float gearTorqueRatio = currentGear == VehicleGear.Drive ? automaticTransmission.TorqueRatio : 1f;

        return motorForce * throttleTorqueRatio * gearTorqueRatio * speedFadeRatio;
    }

    // 크리핑 최고 속도에 가까워질수록 토크를 줄인다. 반대 방향으로 밀리는 중(음수 속도)에는 최대 토크로 버틴다
    float CalculateCreepTorque(float gearDirectionSpeedKmh)
    {
        float creepFadeRatio = Mathf.Clamp01((creepSpeedKmh - gearDirectionSpeedKmh) / Mathf.Max(0.1f, creepSpeedFadeKmh));

        return creepTorque * creepFadeRatio;
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

        ApplyAckermannSteer(currentSteerAngle);
    }

    // 안쪽 바퀴를 더 꺾어 두 앞바퀴가 같은 회전 중심을 돌게 한다.
    // 같은 각도로 꺾으면 두 바퀴가 서로 다른 원을 그리려다 미끄러져 회전반경이 계산보다 커진다
    void ApplyAckermannSteer(float centerSteerAngle)
    {
        bool canApplyAckermann = wheelBaseMeters > 0f && Mathf.Abs(centerSteerAngle) > 0.01f;
        if (!canApplyAckermann)
        {
            frontLeftCollider.steerAngle = centerSteerAngle;
            frontRightCollider.steerAngle = centerSteerAngle;
            return;
        }

        // 뒷축 중앙 기준 회전반경. 바퀴별 각도는 이 회전 중심을 향하도록 계산한다
        float rearAxleTurnRadius = wheelBaseMeters / Mathf.Tan(Mathf.Abs(centerSteerAngle) * Mathf.Deg2Rad);
        float innerWheelAngle = Mathf.Atan(wheelBaseMeters / Mathf.Max(rearAxleTurnRadius - halfTrackMeters, 0.01f)) * Mathf.Rad2Deg;
        float outerWheelAngle = Mathf.Atan(wheelBaseMeters / (rearAxleTurnRadius + halfTrackMeters)) * Mathf.Rad2Deg;

        // WheelCollider는 양수 각도가 우회전이다. 우회전이면 오른쪽 바퀴가 안쪽
        bool isTurningRight = centerSteerAngle > 0f;
        float steerSign = Mathf.Sign(centerSteerAngle);

        frontLeftCollider.steerAngle = steerSign * (isTurningRight ? outerWheelAngle : innerWheelAngle);
        frontRightCollider.steerAngle = steerSign * (isTurningRight ? innerWheelAngle : outerWheelAngle);
    }

    // 제동은 바퀴 제동 토크로만 처리하고, 실제 감속은 타이어 마찰이 결정한다
    void Brake()
    {
        float pedalBrakeTorque = moveInput.Brake * brakeForce;

        // P 기어는 변속기가 구동축(앞바퀴)을 잠그고, 엔진 브레이크도 구동축에 걸린다
        float frontPedalBrakeTorque = pedalBrakeTorque + currentEngineBrakeTorque;
        float frontBrakeTorque = currentGear == VehicleGear.Park ? Mathf.Max(brakeForce, frontPedalBrakeTorque) : frontPedalBrakeTorque;

        // 주차 브레이크는 페달과 별개로 뒷바퀴를 잡는다
        float rearBrakeTorque = Mathf.Max(pedalBrakeTorque * rearBrakeTorqueRatio, GetParkingBrakeTorque());

        frontLeftCollider.brakeTorque = frontBrakeTorque;
        frontRightCollider.brakeTorque = frontBrakeTorque;
        rearLeftCollider.brakeTorque = rearBrakeTorque;
        rearRightCollider.brakeTorque = rearBrakeTorque;
    }

    float GetParkingBrakeTorque()
    {
        if (!parkingBrakeOn)
        {
            return 0f;
        }

        // 정지 상태에서는 완전히 고정하고, 주행 중에는 뒷바퀴가 잠겨 차가 돌지 않도록 일부만 건다
        return IsStopped() ? brakeForce : brakeForce * movingParkingBrakeTorqueRatio;
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
