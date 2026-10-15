using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 버튼에 붙여 두면 클릭 시 버튼 라벨의 Text를 씬 이름으로 보고 해당 씬으로 전환한다.
// 라벨은 자식의 TextMeshPro를 먼저 찾고, 없으면 레거시 UI Text를 쓴다.
// 전환할 씬은 Build Settings(Scenes In Build)에 등록되어 있어야 한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class SceneChanger : MonoBehaviour
{
	[Header("References")]
	[SerializeField] private Button button;
	[SerializeField] private TMP_Text sceneNameLabel; // 비우면 자식에서 찾는다
	[SerializeField] private Text legacySceneNameLabel; // TMP 라벨이 없을 때만 사용

	private void Awake()
	{
		if (button == null)
		{
			button = GetComponent<Button>();
		}

		if (sceneNameLabel == null && legacySceneNameLabel == null)
		{
			AssignLabelFromChildren();
		}
	}

	private void OnEnable()
	{
		button.onClick.AddListener(ChangeScene);
	}

	private void OnDisable()
	{
		button.onClick.RemoveListener(ChangeScene);
	}

	// 버튼 OnClick 외에 다른 이벤트에서도 호출할 수 있다
	public void ChangeScene()
	{
		string targetSceneName = ReadSceneName();

		if (string.IsNullOrEmpty(targetSceneName))
		{
			Debug.LogWarning("[SceneChanger] 버튼 Text가 비어 있거나 연결되지 않아 씬을 전환할 수 없습니다.", this);
			return;
		}

		if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
		{
			Debug.LogError($"[SceneChanger] '{targetSceneName}' 씬을 찾을 수 없습니다. Build Settings에 등록되어 있는지 확인하세요.", this);
			return;
		}

		SceneManager.LoadScene(targetSceneName);
	}

	private void AssignLabelFromChildren()
	{
		sceneNameLabel = GetComponentInChildren<TMP_Text>(true);

		if (sceneNameLabel == null)
		{
			legacySceneNameLabel = GetComponentInChildren<Text>(true);
		}
	}

	private string ReadSceneName()
	{
		if (sceneNameLabel != null)
		{
			return sceneNameLabel.text.Trim();
		}

		if (legacySceneNameLabel != null)
		{
			return legacySceneNameLabel.text.Trim();
		}

		return string.Empty;
	}
}
