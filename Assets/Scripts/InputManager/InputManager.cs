using UnityEngine;
using UnityEngine.InputSystem;

// InputSystem_Actions의 액션 맵 이름과 같아야 한다 (enum 이름으로 맵을 찾는다).
// System 맵(메뉴 키)은 다른 입력을 전환하는 키라 꺼지면 다시 켤 수 없으므로 일부러 넣지 않는다.
public enum InputActionMapType
{
    UI,
    Car
}

// Input System 액션 맵을 enum으로 지정해 켜고 끄는 역할.
// 차량 컴포넌트(CarController, VehicleSystems, GyroScope)는 자기 OnEnable에서 액션을 다시 켜므로,
// 끈 뒤에 생성되거나 다시 활성화된 차량이 있으면 그 이후에 DisableInput(Car)을 호출해야 한다.
public class InputManager : MonoBehaviour
{
    [Header("Input System")]
    [SerializeField] InputActionAsset inputActions; // InputSystem_Actions

    // 지정한 액션 맵이 현재 켜져 있는지
    public bool IsInputEnabled(InputActionMapType mapType)
    {
        InputActionMap actionMap = FindActionMap(mapType);
        return actionMap != null && actionMap.enabled;
    }

    public void EnableInput(InputActionMapType mapType)
    {
        SetInputEnabled(mapType, true);
    }

    public void DisableInput(InputActionMapType mapType)
    {
        SetInputEnabled(mapType, false);
    }

    public void SetInputEnabled(InputActionMapType mapType, bool enabled)
    {
        InputActionMap actionMap = FindActionMap(mapType);
        if (actionMap == null)
        {
            return;
        }

        if (enabled)
        {
            actionMap.Enable();
        }
        else
        {
            actionMap.Disable();
        }

        Debug.Log($"[InputManager] {mapType} 입력 {(enabled ? "켜짐" : "꺼짐")}", this);
    }

    InputActionMap FindActionMap(InputActionMapType mapType)
    {
        if (inputActions == null)
        {
            Debug.LogWarning("[InputManager] InputActionAsset이 연결되지 않았습니다.", this);
            return null;
        }

        InputActionMap actionMap = inputActions.FindActionMap(mapType.ToString());
        if (actionMap == null)
        {
            Debug.LogWarning($"[InputManager] 액션 맵 '{mapType}'을 찾을 수 없습니다.", this);
        }

        return actionMap;
    }
}
