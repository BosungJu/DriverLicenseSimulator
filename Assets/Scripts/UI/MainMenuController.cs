using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class MainMenuController : MonoBehaviour
{
	[SerializeField] private string drivingScenePath = "Assets/Scenes/Level/LevelTestScene.unity";

	private bool isLoading;

	public void StartExam()
	{
		if (isLoading)
		{
			return;
		}

		if (!Application.CanStreamedLevelBeLoaded(drivingScenePath))
		{
			Debug.LogError($"[MainMenu] Driving scene is not enabled in the build scene list: {drivingScenePath}", this);
			return;
		}

		isLoading = true;
		SceneManager.LoadScene(drivingScenePath, LoadSceneMode.Single);
	}
}
