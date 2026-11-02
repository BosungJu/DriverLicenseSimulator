using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
[DisallowMultipleComponent]
public class ReportItemUI : MonoBehaviour
{
	[Header("기록에 표시할 텍스트")]
	[SerializeField] private TMP_Text dateText;
	[SerializeField] private TMP_Text attemptText;
	[SerializeField] private TMP_Text scoreText;

	private Button itemButton;
	private Action selectedAction;

	private void Awake()
	{
		itemButton = GetComponent<Button>();
	}

	private void OnEnable()
	{
		itemButton.onClick.AddListener(HandleClick);
	}

	private void OnDisable()
	{
		if (itemButton != null)
		{
			itemButton.onClick.RemoveListener(HandleClick);
		}
	}

	public void Bind(
		string date,
		int attemptNumber,
		int score,
		Action onSelected
	)
	{
		selectedAction = null;

		if (
			dateText == null ||
			attemptText == null ||
			scoreText == null
		)
		{
			Debug.LogError(
				"[ReportItemUI] 날짜, 회차, 점수 텍스트를 연결해 주세요.",
				this
			);

			return;
		}

		dateText.text = date;
		attemptText.text = $"주행 연습 {attemptNumber}회차";
		scoreText.text = $"{score}점";

		selectedAction = onSelected;
	}

	private void HandleClick()
	{
		selectedAction?.Invoke();
	}
}