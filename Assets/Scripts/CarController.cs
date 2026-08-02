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
    public float accelRate = 1200f; //�ʴ� ��ũ ��·� (���� ���� ��)
    public float releaseRate = 2500f; //�ʴ� ��ũ �϶��� (���� �� ��)
    public float maxSpeedKmh = 110f; // 1�� ���� ���� �ְ��ӵ� ����

    [Header("Steering Settings")]
    public float maxSteerAngle = 32f; //���ӿ����� �ִ� ���Ⱒ
    public float minSteerAngle = 6f; //���ӿ����� �ִ� ���Ⱒ
    public float steerSpeed = 80f; //�ڵ� ����� �ӵ� (�ʴ� ����)
    public float fullSteerSpeedThreshold = 22f; //�� �ӵ�(m/s) �̻��̸� �ּҰ� ����

    float horizontalInput;
    float verticalInput;
    bool isBraking;

    float currentSteerAngle; //���� ���� ���� ���� ����
    float currentMotor; //���� �ɷ��ִ� ���� ��ũ

    Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        rb.centerOfMass = new Vector3(0, -0.7f, 0);
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

        //�ְ� �ӵ� ���� : 110 km ������ ���� X
        float speedKmh = rb.linearVelocity.magnitude * 3.6f;
        if (speedKmh > maxSpeedKmh)
            targetMotor = 0f;

        //��� ���̸� accelRate(���), ���� ���̸� releaseRate(�϶�)
        float rate = Mathf.Abs(targetMotor) > Mathf.Abs(currentMotor) ? accelRate : releaseRate;

        currentMotor = Mathf.MoveTowards(currentMotor, targetMotor, rate * Time.fixedDeltaTime);

        rearLeftCollider.motorTorque = currentMotor;
        rearRightCollider.motorTorque = currentMotor;
    }

    void Steer()
    {
        float speed = rb.linearVelocity.magnitude; //���� �ӵ� (m/s)

        //�ӵ��� �������� �ִ� ���Ⱒ�� �ٿ��� ���� ������ Ȯ��
        float speedFactor = Mathf.Clamp01(speed / fullSteerSpeedThreshold);
        float currentMaxSteer = Mathf.Lerp(maxSteerAngle, minSteerAngle, speedFactor);

        //��ǥ ������ �ʴ� steerSpeed�� �ε巴�� �̵�
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
            rb.linearVelocity *= 0.98f;
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

        wheel.rotation = rot * Quaternion.Euler(0, 0, 90);
    }
}