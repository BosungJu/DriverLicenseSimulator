using UnityEngine;
using UnityEngine.InputSystem;

// 시뮬레이션 중 메뉴 키(System/Menu, ESC)로 메뉴 패널을 열고 닫는다.
// 열면 차량 조작(InputManager의 Car 맵)을 끄고 시간을 멈추며, 닫으면 둘 다 되돌린다.
// 패드에서는 외부 프로그램이 버튼을 ESC로 매핑한다는 전제로 키보드 바인딩만 둔다.
[DisallowMultipleComponent]
public class MenuController : MonoBehaviour
{
	private const float PausedTimeScale = 0f;

	[Header("References")]
	[SerializeField] private InputActionReference menuAction; // System/Menu
	[SerializeField] private InputManager inputManager;
	[SerializeField] private GameObject menuPanel;

	private float timeScaleBeforeOpen = 1f;

	public bool IsMenuOpen { get; private set; }

	private bool HasMenuAction => menuAction != null && menuAction.action != null;

	private void Awake()
	{
		if (menuPanel == null)
		{
			return;
		}

		// 컨트롤러가 패널 안에 있으면 패널을 숨길 때 자신도 꺼져 메뉴 키를 받지 못한다
		if (transform.IsChildOf(menuPanel.transform))
		{
			Debug.LogError("[MenuController] Menu Panel 바깥의 항상 켜져 있는 오브젝트에 붙여야 합니다.", this);
			menuPanel = null;
			return;
		}

		menuPanel.SetActive(false);
	}

	private void OnEnable()
	{
		if (inputManager == null || menuPanel == null)
		{
			Debug.LogWarning("[MenuController] InputManager 또는 Menu Panel이 연결되지 않았습니다.", this);
		}

		if (!HasMenuAction)
		{
			Debug.LogWarning("[MenuController] Menu Action이 연결되지 않아 메뉴 키를 받을 수 없습니다.", this);
			return;
		}

		menuAction.action.Enable();
	}

	private void OnDisable()
	{
		if (HasMenuAction)
		{
			menuAction.action.Disable();
		}

		// 메뉴가 열린 채로 비활성화(씬 전환 등)되면 시간과 조작이 멈춘 상태로 남지 않도록 닫는다
		if (IsMenuOpen)
		{
			CloseMenu();
		}
	}

	// timeScale이 0이어도 Update는 매 프레임 호출되므로 메뉴를 닫는 입력도 받을 수 있다
	private void Update()
	{
		if (HasMenuAction && menuAction.action.WasPressedThisFrame())
		{
			ToggleMenu();
		}
	}

	public void ToggleMenu()
	{
		if (IsMenuOpen)
		{
			CloseMenu();
		}
		else
		{
			OpenMenu();
		}
	}

	public void OpenMenu()
	{
		if (IsMenuOpen)
		{
			return;
		}

		timeScaleBeforeOpen = Time.timeScale;
		Time.timeScale = PausedTimeScale;

		if (inputManager != null)
		{
			inputManager.DisableInput(InputActionMapType.Car);
		}

		if (menuPanel != null)
		{
			menuPanel.SetActive(true);
		}

		IsMenuOpen = true;
	}

	// 메뉴 패널의 "계속하기" 버튼 OnClick에도 연결할 수 있다
	public void CloseMenu()
	{
		if (!IsMenuOpen)
		{
			return;
		}

		if (menuPanel != null)
		{
			menuPanel.SetActive(false);
		}

		if (inputManager != null)
		{
			inputManager.EnableInput(InputActionMapType.Car);
		}

		Time.timeScale = timeScaleBeforeOpen;

		IsMenuOpen = false;
	}
}
