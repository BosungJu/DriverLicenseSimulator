using System.Collections;
using TMPro;
using UnityEngine;

// 코스 지점에 들어설 때 DrivingTestLevel이 보내는 명령(start, hill, left, T_TEST 등)을 받아
// GuidePopup 이미지 위에 시험 과정 안내 문구를 일정 시간 띄운다.
// 여러 지점에 걸친 명령(hill_0~hill_2 등)은 단계마다 다른 안내를 둘 수 있다.
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
		[Tooltip("이 문구를 띄울 단계(hill_1이면 1). -1이면 단계 없는 명령과 첫 단계(_0)에 쓰는 공통 문구다.")]
		public int stepIndex = DrivingSectionAction.NoStep;
		[TextArea] public string message;

		public CommandGuide(string commandName, string message)
			: this(commandName, DrivingSectionAction.NoStep, message)
		{
		}

		public CommandGuide(string commandName, int stepIndex, string message)
		{
			this.commandName = commandName;
			this.stepIndex = stepIndex;
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

	// 장내기능시험 채점기준과 DrivingScoringSettings 기본값(출발 30초, 경사로 통과 30초, 직각주차 120초,
	// 가속 20km/h, 돌발 정지 2초·비상등 3초, 신호 정차 20초)에 맞춘 문구다. 기준을 바꾸면 문구도 함께 고친다.
	[Header("Guides")]
	[SerializeField] private CommandGuide[] commandGuides =
	{
		new CommandGuide("start", "기능시험을 시작합니다. 안전띠를 매고 시동을 건 뒤\n방향지시등을 켜고 30초 안에 출발하세요."),
		new CommandGuide("hill", "경사로 구간입니다.\n정지 구간에 정차한 뒤 뒤로 밀리지 않게 출발하세요."),
		new CommandGuide("hill", 0, "경사로 구간입니다.\n정지 구간까지 천천히 올라가세요."),
		new CommandGuide("hill", 1, "경사로 정지 구간입니다. 완전히 정차한 뒤\n30초 안에 뒤로 밀리지 않게 출발하세요."),
		new CommandGuide("hill", 2, "경사로를 통과했습니다.\n속도를 줄이며 천천히 내려가세요."),
		new CommandGuide("left", "좌회전하세요.\n좌측 방향지시등을 켜고 진행하세요."),
		new CommandGuide("right", "우회전하세요.\n우측 방향지시등을 켜고 진행하세요."),
		new CommandGuide("signal", "신호교차로입니다. 신호를 지키고 앞 범퍼가\n정지선을 넘지 않게 하세요. 20초 이상 서 있지 마세요."),
		new CommandGuide("t_test", "직각주차 구간입니다.\n검지선을 밟지 않고 주차한 뒤 주차 브레이크를 채우세요."),
		new CommandGuide("t_test", 0, "직각주차 구간입니다.\n120초 안에 검지선을 밟지 않고 주차하세요."),
		new CommandGuide("t_test", 1, "후진하여 확인선까지 차고에 넣으세요.\n바퀴가 검지선에 닿지 않게 주의하세요."),
		new CommandGuide("t_test", 2, "주차 브레이크를 채웠다가 풀고\n전진하여 차고를 빠져나오세요."),
		new CommandGuide("t_test", 3, "직각주차 구간을 통과했습니다.\n다음 코스로 진행하세요."),
		new CommandGuide("fast", "가속 구간입니다.\n시속 20km 이상으로 가속한 뒤 감속하세요."),
		new CommandGuide("on_emergency", "돌발 상황입니다! 2초 안에 정지하고\n3초 안에 비상등을 켠 뒤, 끄고 출발하세요."),
		new CommandGuide("end", "종료 지점입니다.\n시험 결과를 확인하세요.")
	};
	[Tooltip("다음 지점이 종료 지점(end)일 때 그 구간 안내 아래에 덧붙인다. 종료 방향지시등은 이 구간에서 채점된다.")]
	[SerializeField] private string finishApproachMessage = "곧 종료 지점입니다. 방향지시등을 켜고 도착하세요.";

	private MapGenerator mapGenerator;
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

		// DrivingTestLevel requires a MapGenerator on the same object
		drivingTestLevel.TryGetComponent(out mapGenerator);

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

		if (IsNextPointFinish() && !string.IsNullOrEmpty(finishApproachMessage))
		{
			message = $"{message}\n{finishApproachMessage}";
		}

		ShowGuide(message);
	}

	// 안내 이벤트 시점에 PassedRoutePointCount는 이미 다음 지점의 인덱스를 가리킨다
	private bool IsNextPointFinish()
	{
		if (mapGenerator == null)
		{
			return false;
		}

		int nextPointIndex = drivingTestLevel.PassedRoutePointCount;
		if (nextPointIndex >= mapGenerator.RouteSections.Count)
		{
			return false;
		}

		MapRouteSection nextSection = mapGenerator.RouteSections[nextPointIndex];
		if (nextSection == null)
		{
			return false;
		}

		foreach (string nextCommand in nextSection.Commands)
		{
			DrivingSectionAction.SplitStep(nextCommand, out string commandName, out _);
			if (string.Equals(commandName, DrivingScoringSettings.FinishCommand, System.StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	// 여러 지점에 걸친 명령(hill_0~hill_2, T_TEST_0~T_TEST_3)은 단계별 문구를 먼저 찾는다.
	// 공통 문구는 단계 없는 명령과 첫 단계에만 써서, 단계 문구가 없는 이어지는 단계에서 같은 문구가 반복되지 않게 한다.
	private bool TryGetGuideMessage(string command, out string message)
	{
		DrivingSectionAction.SplitStep(command, out string commandName, out int stepIndex);

		if (stepIndex != DrivingSectionAction.NoStep && TryFindGuideMessage(commandName, stepIndex, out message))
		{
			return true;
		}

		if (stepIndex <= 0 && TryFindGuideMessage(commandName, DrivingSectionAction.NoStep, out message))
		{
			return true;
		}

		message = string.Empty;
		return false;
	}

	private bool TryFindGuideMessage(string commandName, int stepIndex, out string message)
	{
		foreach (CommandGuide commandGuide in commandGuides)
		{
			bool isMatchingCommand = string.Equals(commandGuide.commandName, commandName, System.StringComparison.OrdinalIgnoreCase);
			if (isMatchingCommand && commandGuide.stepIndex == stepIndex && !string.IsNullOrEmpty(commandGuide.message))
			{
				message = commandGuide.message;
				return true;
			}
		}

		message = string.Empty;
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
