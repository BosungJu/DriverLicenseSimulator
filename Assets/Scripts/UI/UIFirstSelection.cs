using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIFirstSelection : MonoBehaviour
{
	[SerializeField] private Selectable firstSelected;

	private void OnEnable()
	{
		StartCoroutine(SelectFirstNextFrame());
	}

	private IEnumerator SelectFirstNextFrame()
	{
		yield return null;

		EventSystem eventSystem = EventSystem.current;

		if (!isActiveAndEnabled ||
			eventSystem == null ||
			firstSelected == null ||
			!firstSelected.IsActive() ||
			!firstSelected.IsInteractable())
		{
			yield break;
		}

		eventSystem.SetSelectedGameObject(null);
		eventSystem.SetSelectedGameObject(
			firstSelected.gameObject
		);
	}

	private void OnDisable()
	{
		StopAllCoroutines();
	}
}