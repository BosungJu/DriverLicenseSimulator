using JetBrains.Annotations;
using UnityEngine;

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
    public float accelRate = 1200f; //초당 토크 상승량 (엑셀 밟을 때)
    public float releaseRate = 2500f; //초당 토크 하락량 (엑셀 뗄 때)
    public float maxSpeedKmh = 110f; // 1톤 포터 기준 최고속도 제한

    [Header("Steering Settings")]
    public float maxSteerAngle = 32f; //저속에서의 최대 조향각
    public float minSteerAngle = 6f; //고속에서의 최대 조향각
    public float steerSpeed = 80f; //핸들 감기는 속도 (초당 각도)
    public float fullSteerSpeedThreshold = 22f; //이 속도(m/s) 이상이면 최소각 적용

    float horizontalInput;
    float verticalInput;
    bool isBraking;

    float currentSteerAngle; //지금 실제 적용 중인 각도
    float currentMotor; //실제 걸려있는 모터 토크

    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.centerOfMass = new Vector3(0, 0.2f, 0);
    }

    void FixedUpdate()
    {
        GetInput();

        Move();

        Steer();

        Brake();

        UpdateWheels();
    }

    void GetInput()
    {
        horizontalInput = Input.GetAxis("Horizontal");

        verticalInput = Input.GetAxis("Vertical");

        isBraking = Input.GetKey(KeyCode.Space);
    }

    void Move()
    {
        float targetMotor = verticalInput * motorForce;

        //최고 속도 제한 : 110 km 넘으면 가속 X
        float speedKmh = rb.velocity.magnitude * 3.6f;
        if (speedKmh > maxSpeedKmh)
            targetMotor = 0f;

        //밟는 중이면 accelRate(상승), 떼는 중이면 releaseRate(하락)
        float rate = Mathf.Abs(targetMotor) > Mathf.Abs(currentMotor) ? accelRate : releaseRate;

        currentMotor = Mathf.MoveTowards(currentMotor, targetMotor, rate * Time.fixedDeltaTime);

        rearLeftCollider.motorTorque = currentMotor;
        rearRightCollider.motorTorque = currentMotor;
    }

    void Steer()
    {
        float speed = rb.velocity.magnitude; //현재 속도 (m/s)

        //속도가 빠를수록 최대 조향각을 줄여서 고속 안정성 확보
        float speedFactor = Mathf.Clamp01(speed / fullSteerSpeedThreshold);
        float currentMaxSteer = Mathf.Lerp(maxSteerAngle, minSteerAngle, speedFactor);

        //목표 각도로 초당 steerSpeed씩 부드럽게 이동
        float targetSteerAngle = horizontalInput * currentMaxSteer;

        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle,
            targetSteerAngle,
            steerSpeed * Time.fixedDeltaTime
        );

        frontLeftCollider.steerAngle = currentSteerAngle;
        frontRightCollider.steerAngle= currentSteerAngle;
    }

    void Brake()
    {
        float brake = isBraking ? brakeForce : 0f;

        frontLeftCollider.brakeTorque = brake;
        frontRightCollider.brakeTorque = brake;
        rearLeftCollider.brakeTorque = brake;
        rearRightCollider.brakeTorque = brake;

        if(isBraking)
        {
            rb.velocity *= 0.98f;
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