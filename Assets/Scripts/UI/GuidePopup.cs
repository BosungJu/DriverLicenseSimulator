using System.Collections;
using TMPro;
using UnityEngine;

// 코스 지점에 들어설 때 DrivingTestLevel이 보내는 명령(start, hill, left, T_TEST 등)을 받아
// GuidePopup 이미지 위에 시험 과정 안내 문구를 일정 시간 띄운다.
// 한 지점에 명령이 여러 개면("left | on_emergency") 같은 프레임에 차례로 오므로 마지막 안내가 보인다.
// 데이터는 방향 뒤에 특수 구간을 적으므로 돌발·신호·가속 같은 구간 안내가 우선 표시된다.
[DisallowMultipleComponent]
public class GuidePopup : MonoBehaviour
{
	[System.Serializable]
	private class CommandGuide
	{
		[Tooltip("단계 접미사(_0, _1)를 뺀 명령 이름. 대소문자는 구분하지 않는다.")]
		public string commandName;
		[TextArea] public string message;

		public CommandGuide(string commandName, string message)
		{
			this.commandName = commandName;
			this.message = message;
		}
	}

	[Header("References")]
	[Tooltip("비워 두면 씬에서 한 번 찾는다. 프리팹은 씬 참조를 저장할 수 없기 때문이다.")]
	[SerializeField] private DrivingTestLevel drivingTestLevel;
	[Tooltip("GuidePopup 이미지가 붙은 패널. 이 컴포넌트가 붙은 오브젝트의 자식이어야 한다.")]
	[SerializeField] private GameObject popupPanel;
	[SerializeField] private TMP_Text guideText;

	[Header("Display")]
	[Tooltip("안내 문구를 보여 주는 시간(초). 0 이하면 다음 안내가 올 때까지 계속 보인다.")]
	[SerializeField] private float displaySeconds = 4f;

	[Header("Guides")]
	[SerializeField] private CommandGuide[] commandGuides =
	{
		new CommandGuide("start", "기능시험을 시작합니다.\n안전띠를 매고 시동을 건 뒤 출발하세요."),
		new CommandGuide("hill", "경사로 구간입니다.\n정지선에 정차한 뒤 뒤로 밀리지 않게 출발하세요."),
		new CommandGuide("left", "좌회전하세요.\n방향지시등을 켜고 진행하세요."),
		new CommandGuide("right", "우회전하세요.\n방향지시등을 켜고 진행하세요."),
		new CommandGuide("signal", "신호교차로입니다.\n신호에 따라 정지선을 지켜 통과하세요."),
		new CommandGuide("t_test", "직각주차 구간입니다.\n선을 밟지 않고 주차한 뒤 빠져나오세요."),
		new CommandGuide("fast", "가속 구간입니다.\n제한 속도까지 가속한 뒤 감속하세요."),
		new CommandGuide("on_emergency", "돌발 상황입니다!\n즉시 정지하고 비상등을 켜세요."),
		new CommandGuide("end", "종료 지점입니다.\n정지 후 시험 결과를 확인하세요.")
	};

	private Coroutine hideCoroutine;

	private void Awake()
	{
		if (popupPanel == null)
		{
			return;
		}

		// 패널을 숨길 때 이 컴포넌트까지 꺼지면 이벤트를 받지 못한다
		if (popupPanel == gameObject || transform.IsChildOf(popupPanel.transform))
		{
			Debug.LogError("[GuidePopup] Popup Panel의 자식이 아닌, 항상 켜져 있는 부모 오브젝트에 붙여야 합니다.", this);
			popupPanel = null;
			return;
		}

		popupPanel.SetActive(false);
	}

	private void OnEnable()
	{
		if (drivingTestLevel == null)
		{
			drivingTestLevel = FindAnyObjectByType<DrivingTestLevel>();
		}

		if (drivingTestLevel == null || popupPanel == null || guideText == null)
		{
			Debug.LogWarning("[GuidePopup] DrivingTestLevel, Popup Panel 또는 Guide Text가 연결되지 않았습니다.", this);
			return;
		}

		drivingTestLevel.OnInstructionStarted.AddListener(HandleInstructionStarted);
		drivingTestLevel.LevelReset += HideGuide;
	}

	private void OnDisable()
	{
		if (drivingTestLevel != null)
		{
			drivingTestLevel.OnInstructionStarted.RemoveListener(HandleInstructionStarted);
			drivingTestLevel.LevelReset -= HideGuide;
		}

		HideGuide();
	}

	public void ShowGuide(string message)
	{
		if (popupPanel == null || guideText == null)
		{
			return;
		}

		StopHideCoroutine();

		guideText.text = message;
		popupPanel.SetActive(true);

		if (displaySeconds > 0f)
		{
			hideCoroutine = StartCoroutine(HideAfterDelay(displaySeconds));
		}
	}

	public void HideGuide()
	{
		StopHideCoroutine();

		if (popupPanel != null)
		{
			popupPanel.SetActive(false);
		}
	}

	private void HandleInstructionStarted(int pointNumber, string command)
	{
		if (!TryGetGuideMessage(command, out string message))
		{
			return;
		}

		ShowGuide(message);
	}

	// 여러 지점에 걸친 명령(hill_0~hill_2, T_TEST_0~T_TEST_3)은 첫 단계에서만 안내해
	// 같은 구간 안에서 문구가 반복되지 않게 한다.
	private bool TryGetGuideMessage(string command, out string message)
	{
		message = string.Empty;
		DrivingSectionAction.SplitStep(command, out string commandName, out int stepIndex);

		// NoStep(-1)과 첫 단계(0)는 안내하고, 이어지는 단계만 건너뛴다
		if (stepIndex > 0)
		{
			return false;
		}

		foreach (CommandGuide commandGuide in commandGuides)
		{
			bool isMatchingCommand = string.Equals(commandGuide.commandName, commandName, System.StringComparison.OrdinalIgnoreCase);
			if (isMatchingCommand && !string.IsNullOrEmpty(commandGuide.message))
			{
				message = commandGuide.message;
				return true;
			}
		}

		return false;
	}

	// 메뉴로 시간이 멈춘 동안에는 안내도 그대로 남도록 scaled time으로 기다린다
	private IEnumerator HideAfterDelay(float delaySeconds)
	{
		yield return new WaitForSeconds(delaySeconds);

		hideCoroutine = null;
		popupPanel.SetActive(false);
	}

	private void StopHideCoroutine()
	{
		if (hideCoroutine == null)
		{
			return;
		}

		StopCoroutine(hideCoroutine);
		hideCoroutine = null;
	}
}
