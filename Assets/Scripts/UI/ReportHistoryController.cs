using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ReportHistoryController : MonoBehaviour
{
	[Header("기록 목록")]
	[SerializeField] private Transform historyContent;
	[SerializeField] private ReportItemUI itemPrefab;

	[Tooltip("Hierarchy의 Content에 남겨 둔 예시 ReportItem")]
	[SerializeField] private GameObject exampleItem;

	[Header("보고서 파일 로더")]
	[SerializeField] private ReportFileLoader reportFileLoader;

	[Header("오른쪽 공식 기록")]
	[SerializeField] private TMP_Text reportInfoText;
	[SerializeField] private TMP_Text scoreValueText;

	[Header("상세 화면")]
	[SerializeField] private GameObject reportContent;
	[SerializeField] private GameObject emptyBox;

	[Header("저장 폴더")]
	[SerializeField] private string reportsFolderName = "AIReport";

	[Header("목록 탐색")]
	[SerializeField] private Button homeButton;

	private readonly List<ReportItemUI> createdItems =
		new List<ReportItemUI>();

	private string ReportsRootPath =>
		Path.Combine(
			Application.persistentDataPath,
			reportsFolderName
		);

	private sealed class HistoryRecord
	{
		public DateTimeOffset recordedAt;
		public int score;
		public int attemptNumber;
		public string reportPath;
	}

	private void Start()
	{
		RefreshHistory();
	}

	[ContextMenu("보고서 목록 새로고침")]
	public void RefreshHistory()
	{
		if (!Application.isPlaying)
		{
			Debug.LogWarning(
				"[ReportHistory] Play 모드에서 실행해 주세요.",
				this
			);

			return;
		}

		if (!ValidateReferences())
		{
			return;
		}

		try
		{
			List<HistoryRecord> records = ReadRecords();

			ClearCreatedItems();

			if (exampleItem != null && exampleItem.scene.IsValid())
			{
				exampleItem.SetActive(false);
			}

			reportContent.SetActive(false);
			emptyBox.SetActive(true);

			foreach (HistoryRecord record in records)
			{
				ReportItemUI item = Instantiate(
					itemPrefab,
					historyContent
				);

				item.gameObject.SetActive(true);

				string date = record.recordedAt.ToString(
					"yyyy.MM.dd",
					CultureInfo.InvariantCulture
				);

				item.Bind(
					date,
					record.attemptNumber,
					record.score,
					() => ShowReport(record)
				);

				createdItems.Add(item);
			}

			// 생성된 기록과 메인으로 버튼의 이동 순서를 연결한다.
			ConfigureNavigation();

            // 기록을 선택하기 전에는 EmptyBox만 표시한다.
            reportContent.SetActive(false);
            emptyBox.SetActive(true);

            if (records.Count == 0)
            {
                Debug.Log(
                    $"[ReportHistory] 표시할 보고서가 없습니다: {ReportsRootPath}",
                    this
                );
            }
		}
		catch (Exception exception) when (
			exception is IOException ||
			exception is UnauthorizedAccessException ||
			exception is ArgumentException ||
			exception is NotSupportedException
		)
		{
			Debug.LogError(
				$"[ReportHistory] 목록 읽기 실패: {exception.Message}",
				this
			);
		}
	}

	private List<HistoryRecord> ReadRecords()
	{
		var records = new List<HistoryRecord>();

		if (!Directory.Exists(ReportsRootPath))
		{
			return records;
		}

		foreach (string directory in Directory.GetDirectories(ReportsRootPath))
		{
			string reportPath = Path.Combine(
				directory,
				"report.json"
			);

			string resultPath = Path.Combine(
				directory,
				"section_results.json"
			);

			// 두 파일이 모두 있는 생성 완료 기록만 표시한다.
			if (!File.Exists(reportPath) || !File.Exists(resultPath))
			{
				continue;
			}

			try
			{
				JObject result;

				// 날짜를 자동 변환하지 않고 원래 문자열로 읽는다.
				using (var stringReader = new StringReader(
					File.ReadAllText(resultPath)
				))
				using (var jsonReader = new JsonTextReader(stringReader))
				{
					jsonReader.DateParseHandling =
						DateParseHandling.None;

					result = JObject.Load(jsonReader);
				}

				if (
					result["recordedAt"]?.Type != JTokenType.String ||
					result["score"]?.Type != JTokenType.Integer ||
					result["maxScore"]?.Type != JTokenType.Integer
				)
				{
					throw new InvalidDataException(
						"recordedAt, score, maxScore를 확인해 주세요."
					);
				}

				if (!DateTimeOffset.TryParse(
					result["recordedAt"].Value<string>(),
					CultureInfo.InvariantCulture,
					DateTimeStyles.None,
					out DateTimeOffset recordedAt
				))
				{
					throw new InvalidDataException(
						"주행 일시 형식이 올바르지 않습니다."
					);
				}

				int score = result["score"].Value<int>();
				int maxScore = result["maxScore"].Value<int>();

				if (maxScore <= 0 || score < 0 || score > maxScore)
				{
					throw new InvalidDataException(
						"공식 점수 범위가 올바르지 않습니다."
					);
				}

				records.Add(new HistoryRecord
				{
					recordedAt = recordedAt,
					score = score,
					reportPath = reportPath
				});
			}
			catch (Exception exception) when (
				exception is InvalidDataException ||
				exception is IOException ||
				exception is UnauthorizedAccessException ||
				exception is JsonException ||
				exception is FormatException ||
				exception is OverflowException ||
				exception is ArgumentException
			)
			{
				Debug.LogWarning(
					$"[ReportHistory] 기록 제외: {resultPath}\n{exception.Message}",
					this
				);
			}
		}

		// 오래된 기록부터 정렬해 날짜별 회차를 계산한다.
		records.Sort((left, right) =>
		{
			int comparison = left.recordedAt.CompareTo(
				right.recordedAt
			);

			return comparison != 0
				? comparison
				: StringComparer.Ordinal.Compare(
					left.reportPath,
					right.reportPath
				);
		});

		var dailyCounts = new Dictionary<string, int>();

		foreach (HistoryRecord record in records)
		{
			string day = record.recordedAt.ToString(
				"yyyy-MM-dd",
				CultureInfo.InvariantCulture
			);

			dailyCounts.TryGetValue(day, out int count);

			record.attemptNumber = count + 1;
			dailyCounts[day] = record.attemptNumber;
		}

		// 화면에는 최신 기록부터 표시한다.
		records.Reverse();

		return records;
	}

	private void ShowReport(HistoryRecord record)
	{
		reportContent.SetActive(false);
		emptyBox.SetActive(true);

		if (!reportFileLoader.LoadReportFromPath(record.reportPath))
		{
			return;
		}

		string date = record.recordedAt.ToString(
			"yyyy.MM.dd",
			CultureInfo.InvariantCulture
		);

		reportInfoText.text =
			$"{date}  |  주행 연습 {record.attemptNumber}회차";

		scoreValueText.text = $"{record.score}점";
	}

	private void ClearCreatedItems()
	{
		foreach (ReportItemUI item in createdItems)
		{
			if (item != null)
			{
				item.gameObject.SetActive(false);
				Destroy(item.gameObject);
			}
		}

		createdItems.Clear();
	}

	private void ConfigureNavigation()
	{
		if (homeButton == null)
		{
			Debug.LogError(
				"[ReportHistory] Home Button을 연결해 주세요.",
				this
			);

			return;
		}

		var buttons = new List<Button>();

		foreach (ReportItemUI item in createdItems)
		{
			if (item == null)
			{
				continue;
			}

			Button button = item.GetComponent<Button>();

			if (
				button != null &&
				button.isActiveAndEnabled &&
				button.IsInteractable()
			)
			{
				buttons.Add(button);
			}
		}

		for (int i = 0; i < buttons.Count; i++)
		{
			var navigation = new Navigation
			{
				mode = Navigation.Mode.Explicit,

				// 첫 기록에서 위로 이동하면 메인으로 버튼을 선택한다.
				selectOnUp = i > 0
					? buttons[i - 1]
					: homeButton,

				// 마지막 기록에서 아래로 이동하면 메인으로 버튼을 선택한다.
				selectOnDown = i < buttons.Count - 1
					? buttons[i + 1]
					: homeButton
			};

			buttons[i].navigation = navigation;
		}

		var homeNavigation = new Navigation
		{
			mode = Navigation.Mode.Explicit,

			// 메인으로 버튼에서 위로 이동하면 마지막 기록을 선택한다.
			selectOnUp = buttons.Count > 0
				? buttons[buttons.Count - 1]
				: null,

			// 메인으로 버튼에서 아래로 이동하면 첫 기록을 선택한다.
			selectOnDown = buttons.Count > 0
				? buttons[0]
				: null
		};

		homeButton.navigation = homeNavigation;
	}

	private bool ValidateReferences()
	{
		if (
			historyContent == null ||
			itemPrefab == null ||
			reportFileLoader == null ||
			reportInfoText == null ||
			scoreValueText == null ||
			reportContent == null ||
			emptyBox == null
		)
		{
			Debug.LogError(
				"[ReportHistory] Inspector의 필수 항목을 연결해 주세요.",
				this
			);

			return false;
		}

		return true;
	}

	[ContextMenu("저장 폴더 경로 확인")]
	private void ShowReportsFolderPath()
	{
		Directory.CreateDirectory(ReportsRootPath);

		Debug.Log(
			$"[ReportHistory] 저장 폴더: {ReportsRootPath}",
			this
		);
	}
}