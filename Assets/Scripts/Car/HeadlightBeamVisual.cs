using UnityEngine;

public class HeadlightBeamVisual : MonoBehaviour
{
	[Header("연결")]
	public VehicleSystems vehicleSystems;
	public Light leftHeadlight;
	public Light rightHeadlight;

	[Header("전조등 설정")]
	[Min(0f)] public float lowBeamRange = 30f;
	[Min(0f)] public float lowBeamIntensity = 3f;
	[Range(1f, 179f)] public float lowBeamSpotAngle = 60f;
	[Range(0f, 179f)] public float lowBeamInnerAngle = 35f;

	[Header("상향등 설정")]
	[Min(0f)] public float highBeamRange = 70f;
	[Min(0f)] public float highBeamIntensity = 5f;
	[Range(1f, 179f)] public float highBeamSpotAngle = 40f;
	[Range(0f, 179f)] public float highBeamInnerAngle = 25f;

	[Header("전조등 방향에서 추가할 상향등 회전")]
	public Vector3 highBeamRotationOffset = new Vector3(-20f, 0f, 0f);

	private Quaternion leftLowRotation;
	private Quaternion rightLowRotation;

	private bool hasApplied;
	private bool lastHighBeamOn;

	void Awake()
	{
		// 실행 전에 배치한 방향을 전조등 기준으로 저장
		if (leftHeadlight != null)
		{
			leftLowRotation = leftHeadlight.transform.localRotation;
		}

		if (rightHeadlight != null)
		{
			rightLowRotation = rightHeadlight.transform.localRotation;
		}
	}

	void OnEnable()
	{
		hasApplied = false;
	}

	void LateUpdate()
	{
		if (vehicleSystems == null)
		{
			return;
		}

		bool highBeamOn = vehicleSystems.HighBeamOn;

		if (hasApplied && highBeamOn == lastHighBeamOn)
		{
			return;
		}

		ApplyBeam(leftHeadlight, leftLowRotation, highBeamOn);
		ApplyBeam(rightHeadlight, rightLowRotation, highBeamOn);

		lastHighBeamOn = highBeamOn;
		hasApplied = true;
	}

	void ApplyBeam(Light targetLight, Quaternion lowRotation, bool highBeamOn)
	{
		if (targetLight == null)
		{
			return;
		}

		float outerAngle = highBeamOn
			? highBeamSpotAngle
			: lowBeamSpotAngle;

		float innerAngle = highBeamOn
			? highBeamInnerAngle
			: lowBeamInnerAngle;

		targetLight.range = highBeamOn
			? highBeamRange
			: lowBeamRange;

		targetLight.intensity = highBeamOn
			? highBeamIntensity
			: lowBeamIntensity;

		targetLight.spotAngle = outerAngle;
		targetLight.innerSpotAngle = Mathf.Min(innerAngle, outerAngle);

		targetLight.transform.localRotation = highBeamOn
			? lowRotation * Quaternion.Euler(highBeamRotationOffset)
			: lowRotation;
	}
}