using UnityEngine;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

public class WiperController : MonoBehaviour
{
	[Header("Input System 입력")]
	public InputActionReference wiperAction;
	[Header("회전 중심")]
	public Transform leftPivot;
	public Transform rightPivot;

	[Header("왕복 설정")]
	[Range(0f, 150f)]
	public float sweepAngle = 150f;

	[Min(0.01f)]
	public float sweepSpeed = 2.5f;

	[Header("올라가는 방향: 1 또는 -1")]
	public float leftDirection = 1f;
	public float rightDirection = -1f;

	[Header("Animation (선택: 연결하면 직접 회전 대신 사용)")]
	[SerializeField] private Animator wiperAnimator;
	[SerializeField] private string animatorStateName = "Base Layer.Wiper Sweep";
	[SerializeField, Min(0.01f)] private float animationPlaybackSpeed = 1f;

	private bool isWiping;
	private float phase;

	public bool IsWiping => isWiping; // 계기판 표시용

	private Quaternion leftStartRotation;
	private Quaternion rightStartRotation;
	private bool hasStarted;
	private bool isAnimatorReady;
	private int animatorStateHash;
	private float animationDuration;
	private Animator observedAnimator;
	private RuntimeAnimatorController observedAnimatorController;
	private string observedStateName;
	private bool wasAnimatorAvailable;

	public Animator WiperAnimator => wiperAnimator;

	private const float FullCycle = Mathf.PI * 2f;
	void OnEnable()
	{
		if (hasStarted)
		{
			InitializeAnimator();
			ApplyPose(evaluateAnimatorImmediately: true);
		}

		if (wiperAction != null && wiperAction.action != null)
		{
			wiperAction.action.Enable();
		}
	}

	void OnDisable()
	{
		if (wiperAction != null && wiperAction.action != null)
		{
			wiperAction.action.Disable();
		}

		isWiping = false;
		phase = 0f;
		if (hasStarted)
		{
			ApplyPose(evaluateAnimatorImmediately: true);
		}
	}

	void Start()
	{
		if (leftPivot != null)
		{
			leftStartRotation = leftPivot.localRotation;
		}

		if (rightPivot != null)
		{
			rightStartRotation = rightPivot.localRotation;
		}

		hasStarted = true;
		InitializeAnimator();
		ApplyPose(evaluateAnimatorImmediately: true);
	}

	void Update()
	{
		RuntimeAnimatorController currentController = wiperAnimator != null ? wiperAnimator.runtimeAnimatorController : null;
		bool isAnimatorAvailable = wiperAnimator != null && wiperAnimator.isActiveAndEnabled && currentController != null;
		// Retry only on a configuration/availability change, never continuously for an invalid state.
		if (wiperAnimator != observedAnimator || currentController != observedAnimatorController
			|| animatorStateName != observedStateName || isAnimatorAvailable != wasAnimatorAvailable)
		{
			InitializeAnimator();
		}

		if (wiperAction != null
			&& wiperAction.action != null
			&& wiperAction.action.WasPerformedThisFrame()) // Tap 등 Interaction 반영
		{
			SetWiping(!isWiping);
		}

		// 끈 경우에도 진행 중인 왕복은 끝까지 마무리
		if (isWiping || phase > 0f)
		{
			float phaseSpeed = Mathf.Max(0.01f, sweepSpeed);
			if (wiperAnimator != null)
			{
				if (!isAnimatorReady)
				{
					return;
				}

				phaseSpeed = FullCycle / animationDuration * Mathf.Max(0.01f, animationPlaybackSpeed);
			}

			phase += Time.deltaTime * phaseSpeed;

			if (phase >= FullCycle)
			{
				phase = isWiping
					? Mathf.Repeat(phase, FullCycle)
					: 0f;
			}
		}

		ApplyPose();
	}

	public void SetWiping(bool enabled)
	{
		isWiping = enabled && (wiperAnimator == null || isAnimatorReady);
	}

	private void InitializeAnimator()
	{
		phase = 0f;
		isWiping = false;
		isAnimatorReady = false;
		observedAnimator = wiperAnimator;
		observedAnimatorController = wiperAnimator != null ? wiperAnimator.runtimeAnimatorController : null;
		observedStateName = animatorStateName;
		wasAnimatorAvailable = wiperAnimator != null && wiperAnimator.isActiveAndEnabled && observedAnimatorController != null;
		if (wiperAnimator == null)
		{
			return;
		}

		// This component owns the cycle clock so OFF can finish exactly at park.
		wiperAnimator.speed = 0f;
		if (!wiperAnimator.isActiveAndEnabled)
		{
			Debug.LogWarning("[WiperController] The assigned Animator must be enabled and active.", this);
			return;
		}

		animatorStateHash = Animator.StringToHash(animatorStateName);
		if (wiperAnimator.runtimeAnimatorController == null || !wiperAnimator.HasState(0, animatorStateHash))
		{
			Debug.LogWarning($"[WiperController] Animator state '{animatorStateName}' was not found.", this);
			return;
		}

		wiperAnimator.Play(animatorStateHash, 0, 0f);
		wiperAnimator.Update(0f);
		// StateInfo.length becomes infinite at Animator.speed == 0; use the clip's duration.
		AnimatorClipInfo[] clips = wiperAnimator.GetCurrentAnimatorClipInfo(0);
		animationDuration = clips.Length == 1 && clips[0].clip != null ? clips[0].clip.length : 0f;
		isAnimatorReady = animationDuration > 0f;
		if (!isAnimatorReady)
		{
			Debug.LogWarning("[WiperController] The wiper state requires one non-empty animation clip.", this);
		}
	}

	private void ApplyPose(bool evaluateAnimatorImmediately = false)
	{
		if (wiperAnimator != null)
		{
			if (isAnimatorReady && wiperAnimator.isActiveAndEnabled)
			{
				wiperAnimator.Play(animatorStateHash, 0, phase / FullCycle);
				if (evaluateAnimatorImmediately)
				{
					wiperAnimator.Update(0f);
				}
			}

			return;
		}

		// 정지 0° → 최대 각도 → 정지 0°
		float angle =
			(1f - Mathf.Cos(phase)) * 0.5f * sweepAngle;

		if (leftPivot != null)
		{
			float direction = leftDirection >= 0f ? 1f : -1f;

			leftPivot.localRotation =
				leftStartRotation *
				Quaternion.AngleAxis(
					angle * direction,
					Vector3.forward
				);
		}

		if (rightPivot != null)
		{
			float direction = rightDirection >= 0f ? 1f : -1f;

			rightPivot.localRotation =
				rightStartRotation *
				Quaternion.AngleAxis(
					angle * direction,
					Vector3.forward
				);
		}
	}

	[Button]
	public void TestAnimation()
	{
		// 상태 해시와 클립 길이는 Start 이후 InitializeAnimator에서 준비되므로 Play 모드에서만 테스트한다.
		if (!Application.isPlaying)
		{
			Debug.LogWarning("[WiperController] TestAnimation works in Play Mode only.", this);
			return;
		}

		if (wiperAnimator != null && !isAnimatorReady)
		{
			Debug.LogWarning($"[WiperController] Animator is not ready for state '{animatorStateName}'. Check the earlier initialization warning.", this);
			return;
		}

		// Animator.speed가 0으로 고정되어 있어 Play만으로는 움직이지 않으므로 실제 입력과 같은 경로로 토글한다.
		SetWiping(!isWiping);
	}
}
