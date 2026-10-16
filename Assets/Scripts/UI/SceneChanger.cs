using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 버튼에 붙여 두면 클릭 시 Inspector에 입력한 씬 이름으로 전환한다.
// 전환할 씬은 Build Settings(Scenes In Build)에 등록되어 있어야 한다.
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class SceneChanger : MonoBehaviour
{
	[Header("References")]
	[SerializeField] private Button button;

	[Header("Scene")]
	[SerializeField] private string targetSceneName; // Build Settings에 등록된 씬 이름

	private void Awake()
	{
		if (button == null)
		{
			button = GetComponent<Button>();
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
		string sceneName = targetSceneName == null ? string.Empty : targetSceneName.Trim();

		if (string.IsNullOrEmpty(sceneName))
		{
			Debug.LogWarning("[SceneChanger] Target Scene Name이 비어 있어 씬을 전환할 수 없습니다.", this);
			return;
		}

		if (!Application.CanStreamedLevelBeLoaded(sceneName))
		{
			Debug.LogError($"[SceneChanger] '{sceneName}' 씬을 찾을 수 없습니다. Build Settings에 등록되어 있는지 확인하세요.", this);
			return;
		}

		SceneManager.LoadScene(sceneName);
	}
}
