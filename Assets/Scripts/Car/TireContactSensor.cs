using UnityEngine;

// 실제 기능시험처럼 검지선 접촉을 차체가 아니라 타이어 접지면으로 판정하기 위한 센서.
// 차량 Rigidbody의 복합 콜라이더에 섞이면 질량·관성이 바뀌므로 자체 키네마틱 Rigidbody를 가진다.
[RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
public class TireContactSensor : MonoBehaviour
{
	// 타이어 폭과 접지면 길이(m). 검지선은 타이어가 눌러야 반응하므로 바퀴 지름이 아니라 접지면 크기를 쓴다.
	private const float ContactWidthMeters = 0.22f;
	private const float ContactLengthMeters = 0.2f;
	// 노면을 기준으로 위아래로 걸치는 전체 높이(m). 차체 콜라이더 바닥보다 낮게 유지한다.
	private const float ContactHeightMeters = 0.1f;

	private WheelCollider wheel;
	private Rigidbody sensorBody;

	// 이 센서가 속한 차량. 선 접촉 알림에서 센서 대신 차량을 넘기는 데 쓴다.
	public GameObject Vehicle { get; private set; }

	public static TireContactSensor Create(WheelCollider wheel, Rigidbody vehicleBody)
	{
		var sensorObject = new GameObject($"Tire Contact Sensor ({wheel.name})");
		sensorObject.layer = vehicleBody.gameObject.layer;
		sensorObject.transform.SetParent(vehicleBody.transform, false);

		TireContactSensor sensor = sensorObject.AddComponent<TireContactSensor>();
		sensor.Initialize(wheel, vehicleBody.gameObject);
		return sensor;
	}

	public static bool IsTireSensor(Collider collider)
	{
		return collider.TryGetComponent(out TireContactSensor _);
	}

	// 센서면 소속 차량을, 아니면 기존 규칙(붙어 있는 Rigidbody 또는 자기 자신)을 반환한다.
	public static GameObject GetOwnerObject(Collider collider)
	{
		if (collider.TryGetComponent(out TireContactSensor sensor) && sensor.Vehicle != null)
		{
			return sensor.Vehicle;
		}

		return collider.attachedRigidbody != null ? collider.attachedRigidbody.gameObject : collider.gameObject;
	}

	private void Initialize(WheelCollider targetWheel, GameObject vehicle)
	{
		wheel = targetWheel;
		Vehicle = vehicle;

		sensorBody = GetComponent<Rigidbody>();
		sensorBody.isKinematic = true;
		sensorBody.useGravity = false;

		BoxCollider contactTrigger = GetComponent<BoxCollider>();
		contactTrigger.isTrigger = true;
		contactTrigger.size = new Vector3(ContactWidthMeters, ContactHeightMeters, ContactLengthMeters);

		GetContactPose(out Vector3 contactPoint, out Quaternion contactRotation);
		transform.SetPositionAndRotation(contactPoint, contactRotation);
	}

	// 서스펜션 압축과 바퀴별 조향각을 따라 타이어가 노면에 닿는 위치로 옮긴다.
	private void FixedUpdate()
	{
		if (wheel == null || Vehicle == null)
		{
			return;
		}

		GetContactPose(out Vector3 contactPoint, out Quaternion contactRotation);
		sensorBody.MovePosition(contactPoint);
		sensorBody.MoveRotation(contactRotation);
	}

	private void GetContactPose(out Vector3 contactPoint, out Quaternion contactRotation)
	{
		wheel.GetWorldPose(out Vector3 wheelCenter, out _);

		Transform vehicleTransform = Vehicle.transform;
		float wheelRadiusMeters = wheel.radius * wheel.transform.lossyScale.y;

		contactPoint = wheelCenter - vehicleTransform.up * wheelRadiusMeters;
		contactRotation = vehicleTransform.rotation * Quaternion.Euler(0f, wheel.steerAngle, 0f);
	}
}
