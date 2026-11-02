using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class ReportUIController : MonoBehaviour
{
	[Header("화면 영역")]
	[SerializeField] private GameObject reportContent;
	[SerializeField] private GameObject emptyBox;

	[Header("1. 점수 코멘트")]
	[SerializeField] private TMP_Text scoreCommentText;

	[Header("2~4. 능력 점수")]
	[SerializeField] private TMP_Text controlScoreText;
	[SerializeField] private TMP_Text roadDrivingScoreText;
	[SerializeField] private TMP_Text ruleComplianceScoreText;

	[Header("5~7. 분석 본문")]
	[SerializeField] private TMP_Text strengthsText;
	[SerializeField] private TMP_Text improvementsText;
	[SerializeField] private TMP_Text nextPracticeText;

	[Header("능력 점수 막대: BarFill")]
	[SerializeField] private RectTransform controlBarFill;
	[SerializeField] private RectTransform roadDrivingBarFill;
	[SerializeField] private RectTransform ruleComplianceBarFill;

	public bool ApplyJson(string json)
	{
		try
		{
			var textTargets = new Dictionary<UINames, TMP_Text>
			{
				{ UINames.ScoreComment, scoreCommentText },
				{ UINames.ControlScore, controlScoreText },
				{ UINames.RoadDrivingScore, roadDrivingScoreText },
				{ UINames.RuleComplianceScore, ruleComplianceScoreText },
				{ UINames.Strengths, strengthsText },
				{ UINames.Improvements, improvementsText },
				{ UINames.NextPractice, nextPracticeText }
			};

			if (!ValidateReferences(textTargets))
			{
				return false;
			}

			JObject report = JObject.Parse(json);
			Array uiNames = Enum.GetValues(typeof(UINames));

			if (report.Count != uiNames.Length)
			{
				throw new ArgumentException(
					"JSON에는 번호 1~7의 항목만 있어야 합니다."
				);
			}

			var displayTexts = new Dictionary<UINames, string>();
			var scores = new Dictionary<UINames, int>();

			// 모든 값을 먼저 검사한 다음 화면을 변경한다.
			foreach (UINames uiName in uiNames)
			{
				string key = ((int)uiName).ToString(
					CultureInfo.InvariantCulture
				);

				JToken value = report[key];

				if (value == null)
				{
					throw new ArgumentException(
						$"JSON에 {key}번 항목이 없습니다."
					);
				}

				if (IsScoreField(uiName))
				{
					if (value.Type != JTokenType.Integer)
					{
						throw new ArgumentException(
							$"{key}번 항목은 정수여야 합니다."
						);
					}

					int score = value.Value<int>();

					if (score < 0 || score > 100)
					{
						throw new ArgumentException(
							$"{key}번 점수는 0~100이어야 합니다."
						);
					}

					scores.Add(uiName, score);
					displayTexts.Add(
						uiName,
						score.ToString(CultureInfo.InvariantCulture) + "점"
					);
				}
				else
				{
					if (value.Type != JTokenType.String)
					{
						throw new ArgumentException(
							$"{key}번 항목은 문자열이어야 합니다."
						);
					}

					string text = value.Value<string>();

					if (string.IsNullOrWhiteSpace(text))
					{
						throw new ArgumentException(
							$"{key}번 내용이 비어 있습니다."
						);
					}

					displayTexts.Add(uiName, text);
				}
			}

			foreach (UINames uiName in uiNames)
			{
				textTargets[uiName].text = displayTexts[uiName];
			}

			SetBarWidth(
				controlBarFill,
				scores[UINames.ControlScore]
			);

			SetBarWidth(
				roadDrivingBarFill,
				scores[UINames.RoadDrivingScore]
			);

			SetBarWidth(
				ruleComplianceBarFill,
				scores[UINames.RuleComplianceScore]
			);

			emptyBox.SetActive(false);
			reportContent.SetActive(true);

			return true;
		}
		catch (Exception exception) when (
			exception is JsonException ||
			exception is ArgumentException ||
			exception is OverflowException
		)
		{
			Debug.LogError(
				$"[ReportUI] 보고서 표시 실패: {exception.Message}",
				this
			);

			return false;
		}
	}

	private bool ValidateReferences(
		Dictionary<UINames, TMP_Text> textTargets
	)
	{
		if (reportContent == null || emptyBox == null)
		{
			Debug.LogError(
				"[ReportUI] Report Content와 Empty Box를 연결해 주세요.",
				this
			);

			return false;
		}

		foreach (KeyValuePair<UINames, TMP_Text> target in textTargets)
		{
			if (target.Value == null)
			{
				Debug.LogError(
					$"[ReportUI] {target.Key} 텍스트가 연결되지 않았습니다.",
					this
				);

				return false;
			}
		}

		if (
			controlBarFill == null ||
			roadDrivingBarFill == null ||
			ruleComplianceBarFill == null
		)
		{
			Debug.LogError(
				"[ReportUI] 세 개의 BarFill을 모두 연결해 주세요.",
				this
			);

			return false;
		}

		return true;
	}

	private static bool IsScoreField(UINames uiName)
	{
		return uiName == UINames.ControlScore ||
			uiName == UINames.RoadDrivingScore ||
			uiName == UINames.RuleComplianceScore;
	}

	private static void SetBarWidth(RectTransform barFill, int score)
	{
		float ratio = score / 100f;

		// 세로 설정은 유지하고 가로만 부모 너비의 비율로 지정한다.
		barFill.anchorMin = new Vector2(
			0f,
			barFill.anchorMin.y
		);

		barFill.anchorMax = new Vector2(
			ratio,
			barFill.anchorMax.y
		);

		barFill.offsetMin = new Vector2(
			0f,
			barFill.offsetMin.y
		);

		barFill.offsetMax = new Vector2(
			0f,
			barFill.offsetMax.y
		);

		// 0점에서는 Sliced 이미지의 테두리도 보이지 않게 한다.
		barFill.gameObject.SetActive(score > 0);
	}

	[ContextMenu("테스트 보고서 표시")]
	private void ShowTestReport()
	{
		if (!Application.isPlaying)
		{
			Debug.LogWarning(
				"[ReportUI] Play 모드에서 테스트해 주세요.",
				this
			);

			return;
		}

		const string testJson = @"{
			""1"": ""이번 주행의 개선 사항을 확인해 보세요."",
			""2"": 90,
			""3"": 95,
			""4"": 100,
			""5"": ""출발 구간에서 공식 감점이 기록되지 않았어요."",
			""6"": ""경사로 뒤로 밀림과 차로 선 접촉이 기록됐어요."",
			""7"": ""경사로 재출발과 차로 유지 조작을 연습해 보세요.""
		}";

		ApplyJson(testJson);
	}
}